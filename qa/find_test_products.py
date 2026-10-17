"""
Lists the products the test rep can order (rep catalogue) with the operator's agency stock for each,
so you can pick PRODUCT_1_ID / PRODUCT_2_ID for .env.

Run from the QA folder:  python find_test_products.py
Needs in .env: CATALOG_GW, INVENTORY_GW, TOKEN_REP_A, TOKEN_OPERATOR_A (TOKEN_ADMIN_A is used for stock if set).
"""
from sellora_qa import Api, ci_get, items_of, load_env


def main():
    env = load_env(".env")
    catalog, inventory = Api(env["CATALOG_GW"]), Api(env["INVENTORY_GW"])
    products = catalog.get("/api/products/catalogue", env["TOKEN_REP_A"])
    if products.status != 200:
        print(f"Catalogue call failed: {products.status} {products.text[:300]}")
        return
    rows = items_of(products.body)
    owners = inventory.get("/api/inventory-owners", env["TOKEN_OPERATOR_A"])
    agency_owner = next((ci_get(o, "inventoryOwnerId") for o in items_of(owners.body)
                         if ci_get(o, "ownerType") == "Agency"), None)
    stock_token = env.get("TOKEN_ADMIN_A") or env["TOKEN_OPERATOR_A"]
    print(f"Agency inventory owner: {agency_owner}   (put this in AGENCY_A_INVENTORY_OWNER_ID)\n")
    print("Products the rep can order:")
    for product in rows:
        product_id = ci_get(product, "productId")
        available = "?"
        if agency_owner:
            stock = inventory.get(f"/api/stock?productId={product_id}&inventoryOwnerId={agency_owner}", stock_token)
            if stock.status == 200:
                available = sum(ci_get(s, "availableQuantity", 0) for s in items_of(stock.body))
        print(f"  {product_id}  {ci_get(product, 'sku')}  {ci_get(product, 'name')}  "
              f"price {ci_get(product, 'currentUnitPrice')}  agency stock {available}")
    print("\nPick two with agency stock of 60 or more for PRODUCT_1_ID and PRODUCT_2_ID.")


if __name__ == "__main__":
    main()
