"""
Shared helpers for the Sprint 4 QA scripts (E6 Delivery & Returns, E7 Audit & Reporting).

Every script in this folder imports this module. Nothing here talks to a service on its own;
it only gives the scripts one way to:
  - read .env and decode tokens                       load_env, jwt_claims
  - call an API (through APIM or a local service)     Api
  - record PASS / FAIL / WARN / BLOCK / MANUAL lines   Report  (writes evidence/<CSP>/api/*.txt)
  - read and write Kafka topics                        KafkaTap (local Docker container or Confluent Cloud)
  - run SQL against a local Postgres container         Db
  - create delivery jobs the way the real system does  Scenario (Order API path or Kafka-injection path)

Result labels
  PASS    the acceptance criterion holds
  FAIL    the criterion does not hold - raise a bug
  WARN    works, but deviates from the written contract or backlog wording - discuss, may not be a bug
  BLOCK   could not be tested (endpoint not deployed, Kafka not reachable, missing .env value)
  MANUAL  a step a person has to do (mailbox, stopping a slot); the script prints the exact steps
"""
import base64
import json
import os
import pathlib
import re
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta, timezone

SSL_CONTEXT = ssl._create_unverified_context()
COLOMBO = timezone(timedelta(hours=5, minutes=30))
ZERO_GUID = "00000000-0000-0000-0000-000000000000"
DELIVERY_TOPIC = "sellora.delivery.v1"
ORDER_TOPIC = "sellora.order.v1"
DELIVERY_DLQ = "sellora.delivery.dead-letter.v1"
AUDIT_DLQ = "sellora.audit.dead-letter.v1"
DELIVERY_REF_PATTERN = re.compile(r"^DL-\d{6}-[A-Z0-9]{6}$")
RETURN_REF_PATTERN = re.compile(r"^RT-\d{6}-[A-Z0-9]{6}$")

# Field names Notification's DeliveryEventMessage reads (sellora-notification/docs/contracts/delivery-events.v1.md)
NOTIFICATION_DELIVERY_FIELDS = ["eventId", "eventType", "schemaVersion", "companyId", "deliveryId", "orderId",
                                "orderReference", "previousStatus", "status", "occurredAt", "correlationId",
                                "reason", "scheduledFor", "shop", "agency"]
NOTIFICATION_SHOP_FIELDS = ["shopId", "name", "ownerName", "ownerEmail"]
NOTIFICATION_AGENCY_FIELDS = ["agencyId", "name", "email"]
# Additive fields the backlog asks for on DeliveryStatusChanged (US-E6-3-BE4), used by Audit and Reporting
DELIVERY_EXTRA_FIELDS = ["deliveryReference", "territoryId", "provinceId", "actor"]
# Inventory's ReturnAcceptedEvent record (sellora-inventory/.../Application/Events/ReturnAcceptedEvent.cs)
INVENTORY_RETURN_FIELDS = ["eventId", "eventType", "schemaVersion", "companyId", "entityId", "inventoryOwnerId",
                           "returnReference", "lines", "acceptedAt", "correlationId"]


# ---------------------------------------------------------------- small helpers
def load_env(path):
    values = {}
    file = pathlib.Path(path)
    if not file.exists():
        print(f"{path} not found. Copy .env.example to .env and fill it in.")
        sys.exit(2)
    for line in file.read_text(encoding="utf-8").splitlines():
        line = line.strip().replace("\r", "")
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip().strip('"').strip("'")
    return values


def jwt_claims(token):
    try:
        part = token.split(".")[1]
        part += "=" * (-len(part) % 4)
        return json.loads(base64.urlsafe_b64decode(part))
    except (IndexError, ValueError):
        return {}


def company_of(token):
    claims = jwt_claims(token)
    return claims.get("companyId") or claims.get("company_id")


def sub_of(token):
    return jwt_claims(token).get("sub")


def iso(dt):
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def now_utc():
    return datetime.now(timezone.utc)


def colombo_today():
    return datetime.now(COLOMBO).date()


def run_id():
    return datetime.now().strftime("%d%H%M%S")


def ci_get(obj, name, default=None):
    """Case-insensitive dict lookup. Sellora producers differ in JSON casing, consumers are case-insensitive."""
    if not isinstance(obj, dict):
        return default
    if name in obj:
        return obj[name]
    lowered = name.lower()
    for key, value in obj.items():
        if key.lower() == lowered:
            return value
    return default


def missing_fields(obj, names):
    """Names not present on obj, compared case-insensitively."""
    keys = {k.lower() for k in obj.keys()} if isinstance(obj, dict) else set()
    return [n for n in names if n.lower() not in keys]


def not_camel_case(obj):
    """Top-level keys that are not camelCase (e.g. 'EventId'). The contracts document camelCase JSON."""
    return [k for k in obj.keys() if k[:1].isupper()] if isinstance(obj, dict) else []


def text_of(body):
    return json.dumps(body) if not isinstance(body, str) else body


def items_of(body):
    if isinstance(body, dict):
        return ci_get(body, "items") or ci_get(body, "rows") or []
    if isinstance(body, list):
        return body
    return []


def poll(fn, timeout=60, interval=2.0):
    """Call fn until it returns something truthy or the timeout passes. Returns the last value."""
    deadline = time.time() + timeout
    value = fn()
    while not value and time.time() < deadline:
        time.sleep(interval)
        value = fn()
    return value


def pause(message, interactive):
    """Ask the tester to do something by hand (stop a slot, restart Kafka). Returns False when not interactive."""
    if not interactive:
        return False
    print("\n" + "*" * 70)
    print(message)
    print("*" * 70)
    input("Press Enter when done ... ")
    return True


# ---------------------------------------------------------------- HTTP
@dataclass
class Response:
    status: int
    body: object
    ms: float
    headers: dict = field(default_factory=dict)
    raw: bytes = b""

    @property
    def text(self):
        return text_of(self.body)

    def detail(self):
        return ci_get(self.body, "detail") or ci_get(self.body, "message") or ci_get(self.body, "title") or self.text


class Api:
    def __init__(self, base, name="api"):
        base = (base or "").strip().rstrip("/")
        if base and not base.startswith(("http://", "https://")):
            base = "https://" + base
        self.base = base
        self.name = name

    @property
    def configured(self):
        return bool(self.base)

    def call(self, method, path, body=None, token=None, headers=None, soft=False, timeout=30):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        for name, value in (headers or {}).items():
            request.add_header(name, value)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, context=SSL_CONTEXT, timeout=timeout) as response:
                status, raw, response_headers = response.status, response.read(), dict(response.headers)
        except urllib.error.HTTPError as error:
            status, raw, response_headers = error.code, error.read(), dict(error.headers or {})
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            if soft:
                return Response(0, f"unreachable: {getattr(error, 'reason', error)}", 0)
            print(f"\nCannot reach {self.base}{path}: {getattr(error, 'reason', error)}")
            print("Check the URL in .env (staging) or that the local service is running (local).")
            sys.exit(2)
        elapsed_ms = (time.perf_counter() - started) * 1000
        content_type = (response_headers.get("Content-Type") or response_headers.get("content-type") or "").lower()
        payload = raw
        if "json" in content_type or not content_type:
            try:
                payload = json.loads(raw.decode("utf-8-sig") or "null")
            except ValueError:
                payload = raw.decode(errors="replace")
        elif content_type.startswith("text/"):
            payload = raw.decode("utf-8-sig", errors="replace")
        return Response(status, payload, elapsed_ms, response_headers, raw)

    def get(self, path, token=None, **kw):
        return self.call("GET", path, token=token, **kw)

    def post(self, path, body=None, token=None, **kw):
        return self.call("POST", path, body, token=token, **kw)

    def put(self, path, body=None, token=None, **kw):
        return self.call("PUT", path, body, token=token, **kw)

    def patch(self, path, body=None, token=None, **kw):
        return self.call("PATCH", path, body, token=token, **kw)

    def delete(self, path, token=None, **kw):
        return self.call("DELETE", path, token=token, **kw)


# ---------------------------------------------------------------- report / evidence
class Report:
    LABELS = ("passed", "failed", "warned", "blocked", "manual")

    def __init__(self, script_name):
        self.script = script_name
        self.sections = {}
        self.current = None
        self.attachments = []

    def story(self, jira_key, title):
        self.current = jira_key
        self.sections.setdefault(jira_key, {"lines": [], **{k: 0 for k in self.LABELS}})
        self._print(f"\n================ {jira_key}  {title} ================")

    def case(self, qa_id, title):
        self._print(f"-- {qa_id}  {title}")

    def note(self, text):
        self._print(f"  INFO    {text}")

    def check(self, name, condition, detail=""):
        self._count("passed" if condition else "failed")
        self._print(f"  {'PASS' if condition else 'FAIL'}    {name}" + (f"  -> {detail}" if detail and not condition else ""))
        return condition

    def expect_status(self, name, response, *expected):
        ok = response.status in expected
        shown = "/".join(str(e) for e in expected)
        return self.check(f"{name} -> {shown}", ok, f"got {response.status} {response.text[:400]}")

    def warn(self, name, detail=""):
        self._count("warned")
        self._print(f"  WARN    {name}" + (f"  -> {detail}" if detail else ""))

    def blocked(self, name, reason):
        self._count("blocked")
        self._print(f"  BLOCK   {name}  -> {reason}")

    def manual(self, name, steps):
        self._count("manual")
        self._print(f"  MANUAL  {name}")
        for step in steps:
            self._print(f"            - {step}")

    def attach(self, relative_path, content):
        """Save extra evidence (captured Kafka messages, CSV files) next to the text report."""
        self.attachments.append((self.current, relative_path, content))

    def _count(self, label):
        if self.current:
            self.sections[self.current][label] += 1

    def _print(self, text):
        print(text)
        if self.current:
            self.sections[self.current]["lines"].append(text)

    def write(self, evidence_root, target, base):
        root = pathlib.Path(evidence_root)
        for jira_key, section in self.sections.items():
            folder = root / jira_key / "api"
            folder.mkdir(parents=True, exist_ok=True)
            header = [f"Script: {self.script}", f"Target: {target} ({base})",
                      f"Run at: {datetime.now():%Y-%m-%d %H:%M:%S} (Colombo {datetime.now(COLOMBO):%Y-%m-%d %H:%M})"]
            footer = ["", "RESULT: " + ", ".join(f"{section[k]} {k}" for k in self.LABELS)]
            (folder / f"{self.script}-{target}.txt").write_text("\n".join(header + section["lines"] + footer),
                                                               encoding="utf-8")
        for jira_key, relative, content in self.attachments:
            path = root / (jira_key or "misc") / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            if isinstance(content, (bytes, bytearray)):
                path.write_bytes(content)
            else:
                path.write_text(content if isinstance(content, str) else json.dumps(content, indent=2, default=str),
                                encoding="utf-8")

    @property
    def totals(self):
        return {k: sum(s[k] for s in self.sections.values()) for k in self.LABELS}

    def finish(self, evidence_root, target, base):
        self.write(evidence_root, target, base)
        totals = self.totals
        print(f"\nEvidence written to {evidence_root}/<CSP-key>/api/{self.script}-{target}.txt")
        print("RESULT: " + ", ".join(f"{totals[k]} {k}" for k in self.LABELS))
        sys.exit(1 if totals["failed"] else 0)


# ---------------------------------------------------------------- local Postgres (docker exec psql)
class Db:
    def __init__(self, container, database, user):
        self.container, self.database, self.user = container, database, user

    @property
    def configured(self):
        return bool(self.container)

    def rows(self, sql):
        result = subprocess.run(
            ["docker", "exec", self.container, "psql", "-U", self.user, "-d", self.database,
             "-t", "-A", "-F", "|", "-c", sql],
            capture_output=True, text=True, timeout=60)
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip())
        return [line.split("|") for line in result.stdout.splitlines() if line.strip()]

    def scalar(self, sql):
        rows = self.rows(sql)
        return rows[0][0] if rows else None

    def run_as(self, user, sql):
        """Run SQL as another role and return (returncode, combined output). Used for grant checks."""
        result = subprocess.run(
            ["docker", "exec", self.container, "psql", "-U", user, "-d", self.database, "-c", sql],
            capture_output=True, text=True, timeout=60)
        return result.returncode, (result.stdout + result.stderr).strip()


# ---------------------------------------------------------------- Kafka
@dataclass
class KafkaMessage:
    topic: str
    partition: int
    offset: int
    timestamp_ms: int
    key: str
    headers: dict
    value: str

    @property
    def json(self):
        try:
            return json.loads(self.value)
        except (TypeError, ValueError):
            return None

    def header(self, *names):
        for name in names:
            for key, value in self.headers.items():
                if key.lower() == name.lower():
                    return value
        return None

    def as_evidence(self):
        return {"topic": self.topic, "partition": self.partition, "offset": self.offset,
                "timestamp": datetime.fromtimestamp(self.timestamp_ms / 1000, timezone.utc).isoformat(),
                "key": self.key, "headers": self.headers, "value": self.json if self.json is not None else self.value}


class KafkaTap:
    """
    docker mode : uses kafka-console-producer / kafka-console-consumer inside the local Kafka container
                  (LOCAL_KAFKA_CONTAINER, default sellora-kafka from sellora-infra's docker-compose).
    cloud mode  : uses the confluent-kafka Python client against Confluent Cloud
                  (pip install confluent-kafka; KAFKA_BOOTSTRAP, KAFKA_API_KEY, KAFKA_API_SECRET in .env).
                  The QA key needs READ on the topics with a consumer-group prefix of 'qa-'. It never commits
                  offsets, so it does not move any service's consumer group.
    Producing to staging topics is only allowed with --allow-produce.
    """

    def __init__(self, mode, env, allow_produce):
        self.mode = mode
        self.env = env
        self.allow_produce = allow_produce
        self.reason = ""
        self.container = env.get("LOCAL_KAFKA_CONTAINER", "sellora-kafka")
        self.bootstrap_in_container = env.get("LOCAL_KAFKA_BOOTSTRAP", "localhost:9092")

    @staticmethod
    def from_env(env, target, allow_produce=False):
        if target == "local":
            tap = KafkaTap("docker", env, True)
            probe = subprocess.run(["docker", "exec", tap.container, "true"], capture_output=True, text=True)
            if probe.returncode != 0:
                tap.mode, tap.reason = None, f"local Kafka container '{tap.container}' not running"
            return tap
        tap = KafkaTap("cloud", env, allow_produce)
        if not (env.get("KAFKA_BOOTSTRAP") and env.get("KAFKA_API_KEY") and env.get("KAFKA_API_SECRET")):
            tap.mode, tap.reason = None, "KAFKA_BOOTSTRAP / KAFKA_API_KEY / KAFKA_API_SECRET not set in .env"
            return tap
        try:
            import confluent_kafka  # noqa: F401
        except ImportError:
            tap.mode, tap.reason = None, "pip install confluent-kafka (needed to read Confluent Cloud topics)"
        return tap

    @property
    def available(self):
        return self.mode is not None

    @property
    def can_produce(self):
        return self.available and (self.mode == "docker" or self.allow_produce)

    # ---- cloud helpers
    def _cloud_conf(self, extra=None):
        conf = {"bootstrap.servers": self.env["KAFKA_BOOTSTRAP"], "security.protocol": "SASL_SSL",
                "sasl.mechanisms": "PLAIN", "sasl.username": self.env["KAFKA_API_KEY"],
                "sasl.password": self.env["KAFKA_API_SECRET"]}
        conf.update(extra or {})
        return conf

    # ---- produce
    def produce(self, topic, key, value, headers=None):
        if not self.can_produce:
            raise RuntimeError(self.reason or "producing to staging topics needs --allow-produce")
        headers = dict(headers or {"qa-source": "sellora-qa"})
        if self.mode == "docker":
            header_text = ",".join(f"{k}:{v}" for k, v in headers.items())
            line = f"{header_text}\t{key or 'null'}\t{value}\n"
            result = subprocess.run(
                ["docker", "exec", "-i", self.container, "kafka-console-producer",
                 "--bootstrap-server", self.bootstrap_in_container, "--topic", topic,
                 "--property", "parse.key=true", "--property", "key.separator=\t",
                 "--property", "parse.headers=true", "--property", "headers.delimiter=\t",
                 "--property", "headers.separator=,", "--property", "headers.key.separator=:"],
                input=line, capture_output=True, text=True, timeout=60)
            if result.returncode != 0:
                raise RuntimeError(result.stderr.strip())
            return
        from confluent_kafka import Producer
        producer = Producer(self._cloud_conf())
        producer.produce(topic, key=key, value=value, headers=[(k, str(v).encode()) for k, v in headers.items()])
        producer.flush(15)

    # ---- read
    def read(self, topic, since, timeout_ms=8000):
        """All messages on topic with a timestamp at or after `since` (datetime)."""
        since_ms = int(since.timestamp() * 1000) - 2000
        if self.mode == "docker":
            return [m for m in self._docker_read(topic, timeout_ms) if m.timestamp_ms >= since_ms]
        return self._cloud_read(topic, since_ms, timeout_ms)

    def _docker_read(self, topic, timeout_ms):
        result = subprocess.run(
            ["docker", "exec", self.container, "kafka-console-consumer",
             "--bootstrap-server", self.bootstrap_in_container, "--topic", topic, "--from-beginning",
             "--timeout-ms", str(timeout_ms), "--property", "print.timestamp=true",
             "--property", "print.partition=true", "--property", "print.offset=true",
             "--property", "print.headers=true", "--property", "print.key=true",
             "--group", f"qa-sprint4-{uuid.uuid4().hex[:8]}"],
            capture_output=True, text=True, timeout=timeout_ms / 1000 + 60, errors="replace")
        messages = []
        for line in result.stdout.splitlines():
            parts = line.split("\t")
            if len(parts) < 6 or not parts[0].startswith(("CreateTime:", "LogAppendTime:")):
                continue
            timestamp = int(parts[0].split(":", 1)[1])
            partition = int(parts[1].split(":", 1)[1])
            offset = int(parts[2].split(":", 1)[1])
            headers = {}
            if parts[3] != "NO_HEADERS":
                for pair in parts[3].split(","):
                    if ":" in pair:
                        k, v = pair.split(":", 1)
                        headers[k] = v
            messages.append(KafkaMessage(topic, partition, offset, timestamp, parts[4], headers, "\t".join(parts[5:])))
        return messages

    def _cloud_read(self, topic, since_ms, timeout_ms):
        from confluent_kafka import Consumer, TopicPartition
        consumer = Consumer(self._cloud_conf({"group.id": f"qa-sprint4-{uuid.uuid4().hex[:8]}",
                                              "enable.auto.commit": False, "auto.offset.reset": "earliest"}))
        try:
            metadata = consumer.list_topics(topic, timeout=15)
            partitions = [TopicPartition(topic, p, since_ms) for p in metadata.topics[topic].partitions]
            consumer.assign(consumer.offsets_for_times(partitions, timeout=15))
            messages, idle_until = [], time.time() + timeout_ms / 1000
            while time.time() < idle_until:
                msg = consumer.poll(1.0)
                if msg is None or msg.error():
                    continue
                idle_until = time.time() + 3
                headers = {}
                for name, raw in msg.headers() or []:
                    try:
                        headers[name] = raw.decode("utf-8") if raw is not None else None
                    except UnicodeDecodeError:
                        headers[name] = "0x" + raw.hex()
                messages.append(KafkaMessage(topic, msg.partition(), msg.offset(), msg.timestamp()[1],
                                             (msg.key() or b"").decode(errors="replace"), headers,
                                             (msg.value() or b"").decode(errors="replace")))
            return messages
        finally:
            consumer.close()

    def wait_for(self, topic, since, predicate, min_count=1, timeout=60):
        """Poll the topic until at least min_count messages match predicate(message, payload)."""
        deadline = time.time() + timeout
        while True:
            found = [m for m in self.read(topic, since) if predicate(m, m.json)]
            if len(found) >= min_count or time.time() > deadline:
                return found
            time.sleep(3)


def delivery_events(tap, since, order_reference, event_type=None, timeout=45, min_count=1):
    """Delivery events for one order (the key is the order reference)."""
    def match(message, payload):
        if not isinstance(payload, dict):
            return False
        if ci_get(payload, "orderReference") != order_reference and message.key != order_reference:
            return False
        return event_type is None or ci_get(payload, "eventType") == event_type
    return tap.wait_for(DELIVERY_TOPIC, since, match, min_count=min_count, timeout=timeout)


# ---------------------------------------------------------------- personas and scenario builder
PERSONA_KEYS = {
    "admin": "TOKEN_ADMIN_A",           # CompanyAdmin, company A
    "admin_b": "TOKEN_ADMIN_B",         # CompanyAdmin, company B
    "am": "TOKEN_AREA_MANAGER_A",       # AreaManager, company A (province of the test shop)
    "op": "TOKEN_OPERATOR_A",           # AgencyOperator of the test shop's agency
    "op_other": "TOKEN_OPERATOR_OTHER",  # AgencyOperator of another agency, same company
    "rep": "TOKEN_REP_A",               # SalesRep of the test shop's territory
    "rep_other": "TOKEN_REP_OTHER",     # SalesRep of another territory
    "shop": "TOKEN_SHOP_A",             # ShopOwner of the test shop
    "shop_other": "TOKEN_SHOP_OTHER",   # ShopOwner of another shop
}


class Scenario:
    """
    Creates delivery jobs for the tests and wraps the common delivery calls.

    source 'order' (default for staging): the real flow - the rep places an order through the Order API,
        the operator approves it, and the script waits for the delivery job to appear. Nothing synthetic.
    source 'kafka' (default for local): the script publishes an OrderConfirmed / OrderCancelled event to
        sellora.order.v1 in the local Kafka container, using the real IDs of the test shop, agency,
        territory and rep, so the personas' tokens can still act on the job.
    """

    def __init__(self, env, target, report, tap, source, delivery_base):
        self.env, self.target, self.report, self.tap, self.source = env, target, report, tap, source
        self.delivery = Api(delivery_base, "delivery")
        self.order = Api(env.get("ORDER_GW", ""), "order")
        self.org = Api(env.get("ORG_GW", ""), "organization")
        self.inventory = Api(env.get("INVENTORY_GW", ""), "inventory")
        self.t = {name: env.get(key, "") for name, key in PERSONA_KEYS.items()}
        self.run = run_id()
        self.counter = 0
        self.company = company_of(self.t["admin"]) if self.t["admin"] else None
        self.ids = {}
        self.scopes = {}
        self.created_orders = []  # (orderReference, fulfilment, note) for the mailbox checklist

    def token(self, name):
        value = self.t.get(name)
        if not value:
            raise MissingPersona(PERSONA_KEYS[name])
        return value

    def has(self, *names):
        return all(self.t.get(n) for n in names)

    # ---- IDs: .env values win, otherwise ask Organization /api/me/scope for each persona
    def scope(self, persona):
        if not self.t.get(persona):
            return {}
        if persona not in self.scopes:
            response = self.org.get("/api/me/scope", self.t[persona], soft=True) if self.org.configured else None
            self.scopes[persona] = response.body if response and response.status == 200 else {}
        return self.scopes[persona]

    def resolve_ids(self):
        e = self.env
        ids = {
            "shopId": e.get("SHOP_A_ID") or ci_get(self.scope("shop"), "shopId"),
            "shopOtherId": e.get("SHOP_OTHER_ID") or (ci_get(self.scope("shop_other"), "shopId") if self.t["shop_other"] else None),
            "agencyId": e.get("AGENCY_A_ID") or ci_get(self.scope("op"), "agencyId"),
            "repId": e.get("REP_A_ID") or ci_get(self.scope("rep"), "salesRepId"),
            "repOtherId": e.get("REP_OTHER_ID") or (ci_get(self.scope("rep_other"), "salesRepId") if self.t["rep_other"] else None),
            "territoryId": e.get("TERRITORY_A_ID"),
            "provinceId": e.get("PROVINCE_A_ID"),
            "product1": e.get("PRODUCT_1_ID"),
            "product2": e.get("PRODUCT_2_ID"),
        }
        if not ids["territoryId"] and self.t["op"] and self.org.configured:
            reps = self.org.get("/api/sales-reps", self.t["op"], soft=True)
            for rep in items_of(reps.body):
                if ci_get(rep, "salesRepId") == ids["repId"]:
                    ids["territoryId"] = ci_get(ci_get(rep, "currentTerritory") or {}, "territoryId")
        if not ids["provinceId"] and self.t["am"]:
            provinces = ci_get(self.scope("am"), "provinceIds") or []
            ids["provinceId"] = provinces[0] if provinces else None
        self.ids.update(ids)
        return ids

    def need(self, *keys):
        missing = [k for k in keys if not self.ids.get(k)]
        if missing:
            raise MissingPersona(", ".join(missing) + " (set in .env or make ORG_GW reachable)")

    # ---- order references
    def next_reference(self, prefix="ORD-QA"):
        self.counter += 1
        return f"{prefix}{self.run}-{self.counter:02d}"

    # ---- job creation
    def scheduled_job(self, quantity=5, approve=True, product_key="product1"):
        """A Pending delivery job for the test shop. Returns the job detail (or None if approve=False)."""
        if self.source == "order":
            order = self.place_order("ScheduledDelivery", quantity, product_key)
            if not approve:
                return {"order": order}
            response = self.order.put(f"/api/orders/{ci_get(order, 'orderId')}/approval", {"decision": "Approve"},
                                      self.token("op"))
            if response.status not in (200, 204):
                raise SetupError(f"approve order {ci_get(order, 'orderReference')}: {response.status} {response.text}")
            job = self.wait_job(ci_get(order, "orderId"))
            if job is not None:
                job["_order"] = order
            return job
        event = self.order_event("OrderConfirmed", "ScheduledDelivery", quantity, product_key)
        self.publish_order_event(event)
        job = self.wait_job(event["orderId"])
        if job is not None:
            job["_order"] = event
        return job

    def cash_sale_job(self, quantity=2):
        """A cash sale paid at the shop. Returns (job detail or None, order dict, checked-in-at)."""
        if self.source == "order":
            order = self.place_order("ImmediateCashSale", quantity, "product1")
            order_id = ci_get(order, "orderId")
            lat, lng = self.shop_coordinates()
            checkin = self.order.post(f"/api/orders/{order_id}/checkin",
                                      {"latitude": lat, "longitude": lng, "capturedAt": iso(now_utc()),
                                       "accuracyMeters": 5}, self.token("rep"))
            if checkin.status != 200:
                raise SetupError(f"check-in: {checkin.status} {checkin.text} (set SHOP_A_LAT / SHOP_A_LNG)")
            payment = self.order.post(f"/api/orders/{order_id}/payment",
                                      {"amount": ci_get(order, "total"), "method": "Cash"}, self.token("rep"))
            if payment.status not in (200, 201):
                raise SetupError(f"payment: {payment.status} {payment.text}")
            detail = self.order.get(f"/api/orders/{order_id}", self.token("admin")).body
            checkout = ci_get(detail, "checkout") or {}
            checked_in_at = ci_get(ci_get(checkout, "latestCheckIn") or {}, "recordedAt")
            return self.wait_job(order_id), detail, checked_in_at
        checked_in_at = now_utc() - timedelta(minutes=4)
        event = self.order_event("OrderConfirmed", "ImmediateCashSale", quantity, "product1",
                                 checked_in_at=checked_in_at)
        self.publish_order_event(event)
        return self.wait_job(event["orderId"]), event, iso(checked_in_at)

    def shop_coordinates(self):
        """The shop's registered location from Organization GET /api/hierarchy (what Order checks against);
        falls back to SHOP_A_LAT / SHOP_A_LNG in .env."""
        if "shopCoords" not in self.ids:
            found = None
            if self.org.configured:
                tree = self.org.get("/api/hierarchy", self.token("admin"), soft=True)
                wanted = str(self.ids.get("shopId", "")).lower()

                def walk(node):
                    if isinstance(node, dict):
                        ids = [str(v).lower() for k, v in node.items() if k.lower() in ("shopid", "id")]
                        lat, lng = ci_get(node, "latitude"), ci_get(node, "longitude")
                        if wanted in ids and lat is not None and lng is not None:
                            return float(lat), float(lng)
                        for value in node.values():
                            hit = walk(value)
                            if hit:
                                return hit
                    elif isinstance(node, list):
                        for value in node:
                            hit = walk(value)
                            if hit:
                                return hit
                    return None
                found = walk(tree.body) if tree.status == 200 else None
            if found:
                print(f"  INFO    shop location from Organization: {found[0]}, {found[1]}")
            else:
                found = (float(self.env.get("SHOP_A_LAT") or 0), float(self.env.get("SHOP_A_LNG") or 0))
                print(f"  INFO    shop location from .env: {found[0]}, {found[1]}")
            self.ids["shopCoords"] = found
        return self.ids["shopCoords"]

    def place_order(self, fulfilment, quantity, product_key):
        self.need("shopId", product_key)
        body = {"shopId": self.ids["shopId"], "fulfilmentType": fulfilment,
                "lines": [{"productId": self.ids[product_key], "quantity": quantity}]}
        response = self.order.post("/api/orders", body, self.token("rep"))
        if response.status != 201:
            raise SetupError(f"place {fulfilment} order: {response.status} {response.text[:500]}")
        self.created_orders.append((ci_get(response.body, "orderReference"), fulfilment))
        return response.body

    def cancel_order(self, order, reason="QA cancellation", source="ShopCancellation"):
        """Shop owner cancels (order path) or an OrderCancelled event is published (kafka path)."""
        if self.source == "order":
            response = self.order.post(f"/api/orders/{ci_get(order, 'orderId')}/cancellation", {"reason": reason},
                                       self.token("shop"))
            return response
        event = dict(order)
        event.update({"eventId": str(uuid.uuid4()), "eventType": "OrderCancelled", "status": "Cancelled",
                      "occurredAt": iso(now_utc()), "cancelledAt": iso(now_utc()), "reason": reason,
                      "source": source, "cancelledBy": {"userId": sub_of(self.t["shop"]) or "qa", "role": "ShopOwner"}})
        self.publish_order_event(event)
        return Response(204, None, 0)

    def order_event(self, event_type, fulfilment, quantity, product_key, checked_in_at=None, confirmed_at=None,
                    agency_id=None, shop_id=None, total_unit_price=100.0):
        """An order event shaped exactly like sellora-order/docs/contracts/order-events.v1.md."""
        self.need("shopId", "agencyId", "territoryId", "provinceId", "repId", product_key)
        reference = self.next_reference()
        order_id = str(uuid.uuid4())
        confirmed = confirmed_at or now_utc()
        line_total = round(total_unit_price * quantity, 2)
        self.created_orders.append((reference, fulfilment))
        return {
            "eventId": str(uuid.uuid4()), "eventType": event_type, "schemaVersion": "1.0",
            "companyId": self.company, "entityId": order_id, "orderId": order_id, "orderReference": reference,
            "reservationId": str(uuid.uuid4()), "occurredAt": iso(confirmed),
            "correlationId": f"qa-{self.run}-{self.counter}", "fulfilmentType": fulfilment,
            "status": "Confirmed", "orderDate": iso(confirmed - timedelta(minutes=10)),
            "shop": {"shopId": shop_id or self.ids["shopId"], "name": self.env.get("SHOP_A_NAME", "QA Test Shop"),
                     "ownerName": self.env.get("SHOP_A_OWNER_NAME", "QA Shop Owner"),
                     "ownerEmail": self.env.get("SHOP_A_OWNER_EMAIL", "qa.shop@example.com")},
            "agency": {"agencyId": agency_id or self.ids["agencyId"], "name": self.env.get("AGENCY_A_NAME", "QA Agency"),
                       "email": self.env.get("AGENCY_A_EMAIL", "qa.agency@example.com")},
            "territoryId": self.ids["territoryId"], "provinceId": self.ids["provinceId"],
            "salesRep": {"salesRepId": self.ids["repId"], "name": "QA Rep"},
            "lines": [{"productId": self.ids[product_key], "productName": f"QA Product {product_key}",
                       "quantity": quantity, "unitPrice": total_unit_price, "lineTotal": line_total}],
            "subtotal": line_total, "total": line_total, "currency": "LKR",
            "checkoutLocation": None if checked_in_at is None else {
                "latitude": 6.9271, "longitude": 79.8612, "distanceMeters": 12.0, "accuracyMeters": 5.0,
                "checkedInAt": iso(checked_in_at)},
            "confirmedAt": iso(confirmed),
        }

    def publish_order_event(self, event, raw=None):
        if not self.tap.can_produce:
            raise SetupError(f"cannot publish to {ORDER_TOPIC}: {self.tap.reason or 'use --allow-produce'}")
        headers = {"event-id": event.get("eventId", ""), "event-type": event.get("eventType", ""),
                   "schema-version": "1.0", "company-id": event.get("companyId", ""),
                   "correlation-id": event.get("correlationId", "")}
        self.tap.produce(ORDER_TOPIC, event.get("orderReference"), raw or json.dumps(event, separators=(",", ":")),
                         headers)

    def wait_job(self, order_id, timeout=60):
        """Wait until Delivery has a job for the order, then return its detail."""
        def find():
            response = self.delivery.get(f"/api/deliveries?orderId={order_id}&includeHandovers=true",
                                         self.token("admin"), soft=True)
            found = items_of(response.body) if response.status == 200 else []
            return found[0] if found else None
        item = poll(find, timeout=timeout)
        if not item:
            return None
        return self.detail(ci_get(item, "id"))

    def jobs_for_order(self, order_id):
        response = self.delivery.get(f"/api/deliveries?orderId={order_id}&includeHandovers=true&pageSize=100",
                                     self.token("admin"))
        if response.status != 200:
            raise SetupError(f"GET /api/deliveries as Company Admin answered {response.status} {response.text[:200]}")
        return ci_get(response.body, "totalCount", 0)

    # ---- delivery calls
    def detail(self, job_id, persona="admin"):
        response = self.delivery.get(f"/api/deliveries/{job_id}", self.token(persona))
        return response.body if response.status == 200 else None

    def assign(self, job_id, rep_id=None, scheduled=None, persona="op"):
        scheduled = scheduled or (colombo_today() + timedelta(days=1))
        return self.delivery.put(f"/api/deliveries/{job_id}/assignment",
                                 {"salesRepId": rep_id or self.ids["repId"], "scheduledDate": str(scheduled)},
                                 self.token(persona))

    def set_status(self, job_id, status, reason=None, persona="rep", version=None):
        if version is None:
            current = self.detail(job_id) or {}
            version = ci_get(current, "version", 0)
        body = {"status": status, "version": version}
        if reason is not None:
            body["reason"] = reason
        return self.delivery.patch(f"/api/deliveries/{job_id}/status", body, self.token(persona))

    def assigned_job(self, quantity=5):
        job = self.scheduled_job(quantity)
        if not job:
            raise SetupError("delivery job did not appear within 60 s")
        response = self.assign(ci_get(job, "id"))
        if response.status != 204:
            raise SetupError(f"assign: {response.status} {response.text}")
        detail = self.detail(ci_get(job, "id"))
        detail["_order"] = job.get("_order")
        return detail

    def delivered_job(self, quantity=5):
        job = self.assigned_job(quantity)
        job_id = ci_get(job, "id")
        for status in ("InTransit", "Delivered"):
            response = self.set_status(job_id, status)
            if response.status != 204:
                raise SetupError(f"move to {status}: {response.status} {response.text}")
        detail = self.detail(job_id)
        detail["_order"] = job.get("_order")
        return detail

    def mailbox_checklist(self):
        return [f"{reference} ({fulfilment})" for reference, fulfilment in self.created_orders]


class MissingPersona(Exception):
    pass


class SetupError(Exception):
    pass


def standard_args(parser, default_source=None):
    parser.add_argument("--target", choices=["local", "staging"], required=True)
    parser.add_argument("--env", default=".env")
    parser.add_argument("--evidence", default="evidence")
    parser.add_argument("--source", choices=["order", "kafka"], default=default_source,
                        help="how delivery jobs are created (default: order on staging, kafka on local)")
    parser.add_argument("--allow-produce", action="store_true",
                        help="allow publishing test messages to STAGING Kafka topics (malformed / replay tests)")
    parser.add_argument("--interactive", action="store_true",
                        help="pause for manual steps (stop a slot, restart Kafka) instead of marking them MANUAL")
    return parser


def delivery_base(env, target):
    return env.get("DELIVERY_LOCAL", "http://localhost:5297") if target == "local" else env.get("DELIVERY_GW", "")


def audit_base(env, target):
    return env.get("AUDIT_LOCAL", "http://localhost:5298") if target == "local" else env.get("AUDIT_GW", "")


def check_tokens(env):
    """Stop early with a clear message when a token is an ID token or has no companyId / roles claim."""
    problems = []
    for persona, key in PERSONA_KEYS.items():
        token = env.get(key, "")
        if not token:
            continue
        claims = jwt_claims(token)
        if not claims:
            problems.append(f"{key}: not a JWT (copy only the part after 'Bearer ')")
        elif "at_hash" in claims or "c_hash" in claims:
            problems.append(f"{key}: this is the ID token. Copy the ACCESS token instead (see RUN-GUIDE.txt)")
        elif not (claims.get("companyId") or claims.get("company_id")):
            problems.append(f"{key}: no companyId claim - not a Sellora access token")
        elif claims.get("exp") and datetime.fromtimestamp(claims["exp"], timezone.utc) < now_utc():
            problems.append(f"{key}: expired at {datetime.fromtimestamp(claims['exp'], COLOMBO):%H:%M} Colombo time")
    if problems:
        print("\nToken problems in .env:")
        for problem in problems:
            print("  - " + problem)
        print("\nHow to get an access token: log in, DevTools > Network, click a request to the API gateway "
              "(...:8244/sellora-.../api/...), Request Headers > Authorization, copy what follows 'Bearer '.")
        sys.exit(2)


def preflight(s):
    """One call to every API the run depends on, so a wrong gateway URL or an unsubscribed API shows up
    as one clear line instead of a page of FAIL/BLOCK."""
    print("Preflight:")
    checks = [("Delivery GET /api/deliveries", s.delivery, "/api/deliveries?pageSize=1", "admin")]
    if s.source == "order":
        checks.append(("Order GET /api/orders", s.order, "/api/orders?pageSize=1", "admin"))
    checks.append(("Organization GET /api/me/scope (shop owner)", s.org, "/api/me/scope", "shop"))
    broken = False
    for label, api, path, persona in checks:
        if not api.configured or not s.t.get(persona):
            print(f"  skip  {label} (URL or token not set)")
            continue
        response = api.get(path, s.t[persona], soft=True)
        ok = response.status == 200
        broken |= not ok and api is s.delivery
        print(f"  {'ok  ' if ok else 'FAIL'}  {label} -> {response.status}" + ("" if ok else f"  {response.text[:250]}"))
    print(f"  ids   shop {s.ids.get('shopId')}  agency {s.ids.get('agencyId')}  rep {s.ids.get('repId')}  "
          f"territory {s.ids.get('territoryId')}  province {s.ids.get('provinceId')}")
    if broken:
        probe = s.delivery.get(checks[0][2], s.t[checks[0][3]], soft=True)
        status = probe.status
        if status == 401 and "900901" in probe.text:
            print("\nThe API Manager gateway itself rejected the Company Admin token (900901 Invalid Credentials).\n"
                  "The token is revoked or expired: logging out of the web app, or logging in again as the same user,\n"
                  "revokes the token you copied. Copy fresh tokens and do not log out of those browser sessions.\n"
                  "Nothing was written to the evidence folders.")
            sys.exit(2)
        if status == 401:
            reason = ("Delivery API answered 401 to a valid Company Admin access token (Order and Organization accept it). "
                      "The service cannot validate WSO2 tokens on staging - BUG-01 in BUGS.txt.")
        elif status == 0:
            reason = "Delivery API unreachable - check DELIVERY_GW and that the App Service is running."
        elif status == 404:
            reason = "Gateway answered 404 - the Delivery API or this resource is not published in APIM (re-import the OpenAPI)."
        elif status >= 500:
            reason = f"Delivery API answered {status} - the App Service is down or failing (check its Log stream)."
        else:
            reason = f"Delivery API answered {status} to GET /api/deliveries as Company Admin."
        print("\n" + reason)
        stop_blocked(s.report, reason)


RUN_CONTEXT = {}


def stop_blocked(report, reason):
    """Setup failed before any case ran: write a BLOCK evidence file for the script's story, then stop."""
    story = getattr(sys.modules.get("__main__"), "STORY", None)
    if story and RUN_CONTEXT:
        report.story(story, "not run - setup blocked")
        report.blocked("ALL CASES", reason)
        report.write(RUN_CONTEXT["evidence"], RUN_CONTEXT["target"], RUN_CONTEXT["base"])
        print(f"\nBLOCK evidence written to {RUN_CONTEXT['evidence']}/{story}/api/{report.script}-{RUN_CONTEXT['target']}.txt")
    sys.exit(2)


def require_api(args, report, base, key):
    """Stop with BLOCK evidence when the API this script tests has no URL in .env."""
    if not base:
        RUN_CONTEXT.update(evidence=args.evidence, target=args.target, base="not set")
        stop_blocked(report, f"{key} not set in .env - the Audit API (audit, reports, export) is not published in APIM yet.")


def tampered(token):
    """The token with one character in the middle of its signature changed. Changing the last character is not
    enough: its low bits are base64 padding, so many JWT libraries decode it to the same signature."""
    head, _, signature = token.rpartition(".")
    if not head or len(signature) < 10:
        return token + "x"
    i = len(signature) // 2
    swap = "A" if signature[i] != "A" else "B"
    return f"{head}.{signature[:i]}{swap}{signature[i + 1:]}"


def audit_preflight(s, report, base):
    """One call to the Audit API before any audit, report or export case. A 401 or 404 here would turn every
    case into the same FAIL, so the script stops with one clear BLOCK line instead."""
    probe = Api(base, "audit").get("/api/audit?pageSize=1", s.t.get("admin"), soft=True)
    ok = probe.status == 200
    print(f"  {'ok  ' if ok else 'FAIL'}  Audit GET /api/audit -> {probe.status}" + ("" if ok else f"  {probe.text[:250]}"))
    if ok:
        return
    if probe.status == 401 and "900901" in probe.text:
        print("\nThe API Manager gateway rejected the Company Admin token (900901): it is revoked or expired.\n"
              "Copy fresh tokens and do not log out of those browser sessions. Nothing was written.")
        sys.exit(2)
    RUN_CONTEXT["base"] = base
    if probe.status == 401:
        reason = ("Audit API answered 401 with an empty body to a valid Company Admin access token that Delivery, Order "
                  "and Organization accept. The Audit service cannot validate WSO2 tokens on staging - BUG-04 in BUGS.txt. "
                  "Every audit, report and export case would fail for this one reason, so none were run.")
    elif probe.status == 404:
        reason = ("GET /api/audit answered 404 at AUDIT_GW - the Audit API is not published in APIM under this context, "
                  "or AUDIT_GW in .env is wrong.")
    else:
        reason = f"GET /api/audit answered {probe.status} {probe.text[:200]}"
    stop_blocked(report, reason)


def build_scenario(args, env, report, base):
    RUN_CONTEXT.update(evidence=args.evidence, target=args.target, base=base or "not set")
    if not base:
        stop_blocked(report, "API URL not set in .env (AUDIT_GW for audit and reports) - the API is not published in APIM yet.")
    check_tokens(env)
    tap = KafkaTap.from_env(env, args.target, args.allow_produce)
    source = args.source or ("order" if args.target == "staging" else "kafka")
    if source == "kafka" and not tap.can_produce:
        print(f"Kafka-injection source not possible ({tap.reason or 'needs --allow-produce on staging'}); "
              "falling back to the Order API path.")
        source = "order"
    scenario = Scenario(env, args.target, report, tap, source, base)
    scenario.resolve_ids()
    preflight(scenario)
    print(f"Run ID: {scenario.run}   company: {scenario.company}   job source: {source}   "
          f"kafka: {tap.mode or 'unavailable - ' + tap.reason}")
    return scenario


def delivery_db(env):
    return Db(env.get("LOCAL_DELIVERY_DB_CONTAINER", "sellora-delivery-db"),
              env.get("LOCAL_DELIVERY_DB_NAME", "delivery_db"), env.get("LOCAL_DELIVERY_DB_USER", "sellora"))


def wait_seconds(seconds, why):
    print(f"  ...   waiting {seconds} s ({why})")
    time.sleep(seconds)


def env_flag(name):
    return os.environ.get(name, "").lower() in ("1", "true", "yes")


def run_case(report, qa_id, title, fn, *args):
    """Run one QA sub-task; a missing persona or failed setup marks it BLOCK instead of crashing the run."""
    report.case(qa_id, title)
    try:
        fn(*args)
    except MissingPersona as error:
        report.blocked(qa_id, f"missing in .env: {error}")
    except SetupError as error:
        report.blocked(qa_id, f"setup failed: {error}")
    except RuntimeError as error:
        report.blocked(qa_id, f"tooling error: {error}")
    except Exception as error:  # keep the run going; the evidence shows which case broke and why
        report.blocked(qa_id, f"script error {type(error).__name__}: {error}")


def parse_instant(value):
    """ISO-8601 with any offset and up to 7 fraction digits (.NET) -> aware datetime; naive values are UTC."""
    if value is None:
        return None
    text = str(value).strip().replace("Z", "+00:00")
    if "." in text:
        head, tail = text.split(".", 1)
        digits = ""
        for ch in tail:
            if not ch.isdigit():
                break
            digits += ch
        text = f"{head}.{digits[:6].ljust(6, '0')}{tail[len(digits):]}"
    parsed = datetime.fromisoformat(text)
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


def same_instant(a, b, tolerance_seconds=2):
    """Compare two ISO-8601 timestamps (any offset) to within a tolerance."""
    first, second = parse_instant(a), parse_instant(b)
    if first is None or second is None:
        return False
    return abs((first - second).total_seconds()) <= tolerance_seconds


def container_log_contains(container, since, *needles):
    """True/False when the container's log since `since` contains all needles; None when no container given."""
    if not container:
        return None
    result = subprocess.run(["docker", "logs", "--since", iso(since), container],
                            capture_output=True, text=True, errors="replace", timeout=60)
    text = result.stdout + result.stderr
    return all(needle in text for needle in needles)
