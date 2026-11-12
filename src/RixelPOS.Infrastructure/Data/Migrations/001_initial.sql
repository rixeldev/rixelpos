-- RixelPOS initial schema (version 1)
-- All monetary values are integer cents. Quantities are integer thousandths.

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sequences (
    name  TEXT PRIMARY KEY,
    value INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id            TEXT PRIMARY KEY,
    username      TEXT NOT NULL UNIQUE,
    full_name     TEXT NOT NULL,
    role          TEXT NOT NULL,
    pin_hash      TEXT,
    password_hash TEXT,
    is_active     INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS categories (
    id         TEXT PRIMARY KEY,
    name       TEXT NOT NULL UNIQUE,
    color      TEXT NOT NULL DEFAULT '#00C2A8',
    is_active  INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tax_rates (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    rate_bp     INTEGER NOT NULL DEFAULT 0,
    is_inclusive INTEGER NOT NULL DEFAULT 0,
    is_default  INTEGER NOT NULL DEFAULT 0,
    is_active   INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS products (
    id            TEXT PRIMARY KEY,
    sku           TEXT NOT NULL UNIQUE,
    name          TEXT NOT NULL,
    category_id   TEXT REFERENCES categories(id) ON DELETE SET NULL,
    cost_cents    INTEGER NOT NULL DEFAULT 0,
    price_cents   INTEGER NOT NULL DEFAULT 0,
    tax_rate_bp   INTEGER NOT NULL DEFAULT 0,
    tax_inclusive INTEGER NOT NULL DEFAULT 0,
    stock_milli   INTEGER NOT NULL DEFAULT 0,
    min_stock_milli INTEGER NOT NULL DEFAULT 0,
    unit          TEXT NOT NULL DEFAULT 'unit',
    is_favorite   INTEGER NOT NULL DEFAULT 0,
    is_active     INTEGER NOT NULL DEFAULT 1,
    track_stock   INTEGER NOT NULL DEFAULT 1,
    image_path    TEXT,
    notes         TEXT,
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_products_name ON products(name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_products_category ON products(category_id);
CREATE INDEX IF NOT EXISTS ix_products_active ON products(is_active);

CREATE TABLE IF NOT EXISTS product_barcodes (
    id         TEXT PRIMARY KEY,
    product_id TEXT NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    code       TEXT NOT NULL UNIQUE,
    is_primary INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_barcodes_product ON product_barcodes(product_id);
CREATE INDEX IF NOT EXISTS ix_barcodes_code ON product_barcodes(code);

CREATE TABLE IF NOT EXISTS customers (
    id                 TEXT PRIMARY KEY,
    name               TEXT NOT NULL,
    phone              TEXT,
    tax_id             TEXT,
    address            TEXT,
    email              TEXT,
    credit_limit_cents INTEGER NOT NULL DEFAULT 0,
    balance_cents      INTEGER NOT NULL DEFAULT 0,
    store_credit_cents INTEGER NOT NULL DEFAULT 0,
    notes              TEXT,
    is_active          INTEGER NOT NULL DEFAULT 1,
    created_at         TEXT NOT NULL,
    updated_at         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_customers_name ON customers(name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS suppliers (
    id           TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    contact_name TEXT,
    phone        TEXT,
    email        TEXT,
    address      TEXT,
    tax_id       TEXT,
    notes        TEXT,
    is_active    INTEGER NOT NULL DEFAULT 1,
    created_at   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_suppliers_name ON suppliers(name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS cash_sessions (
    id             TEXT PRIMARY KEY,
    opened_at      TEXT NOT NULL,
    closed_at      TEXT,
    opening_cents  INTEGER NOT NULL DEFAULT 0,
    expected_cents INTEGER NOT NULL DEFAULT 0,
    closing_cents  INTEGER,
    difference_cents INTEGER,
    opened_by      TEXT,
    opened_by_name TEXT NOT NULL DEFAULT '',
    closed_by      TEXT,
    closed_by_name TEXT,
    notes          TEXT
);
CREATE INDEX IF NOT EXISTS ix_cash_sessions_open ON cash_sessions(closed_at);

CREATE TABLE IF NOT EXISTS sales (
    id                  TEXT PRIMARY KEY,
    number              INTEGER NOT NULL DEFAULT 0,
    status              TEXT NOT NULL,
    cashier_id          TEXT,
    cashier_name        TEXT NOT NULL DEFAULT '',
    customer_id         TEXT,
    customer_name       TEXT,
    created_at          TEXT NOT NULL,
    completed_at        TEXT,
    subtotal_cents      INTEGER NOT NULL DEFAULT 0,
    discount_cents      INTEGER NOT NULL DEFAULT 0,
    tax_cents           INTEGER NOT NULL DEFAULT 0,
    total_cents         INTEGER NOT NULL DEFAULT 0,
    paid_cents          INTEGER NOT NULL DEFAULT 0,
    change_cents        INTEGER NOT NULL DEFAULT 0,
    sale_discount_type  TEXT NOT NULL DEFAULT 'None',
    sale_discount_value INTEGER NOT NULL DEFAULT 0,
    note                TEXT,
    held_label          TEXT,
    session_id          TEXT REFERENCES cash_sessions(id) ON DELETE SET NULL,
    is_credit           INTEGER NOT NULL DEFAULT 0,
    voided_at           TEXT,
    voided_by           TEXT,
    void_reason         TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_number ON sales(number) WHERE number > 0;
CREATE INDEX IF NOT EXISTS ix_sales_created ON sales(created_at);
CREATE INDEX IF NOT EXISTS ix_sales_completed ON sales(completed_at);
CREATE INDEX IF NOT EXISTS ix_sales_status ON sales(status);
CREATE INDEX IF NOT EXISTS ix_sales_session ON sales(session_id);
CREATE INDEX IF NOT EXISTS ix_sales_customer ON sales(customer_id);

CREATE TABLE IF NOT EXISTS sale_lines (
    id              TEXT PRIMARY KEY,
    sale_id         TEXT NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
    product_id      TEXT,
    sku             TEXT NOT NULL DEFAULT '',
    name            TEXT NOT NULL DEFAULT '',
    quantity_milli  INTEGER NOT NULL DEFAULT 1000,
    unit_price_cents INTEGER NOT NULL DEFAULT 0,
    cost_cents      INTEGER NOT NULL DEFAULT 0,
    discount_type   TEXT NOT NULL DEFAULT 'None',
    discount_value  INTEGER NOT NULL DEFAULT 0,
    net_cents       INTEGER NOT NULL DEFAULT 0,
    tax_cents       INTEGER NOT NULL DEFAULT 0,
    total_cents     INTEGER NOT NULL DEFAULT 0,
    tax_rate_bp     INTEGER NOT NULL DEFAULT 0,
    tax_inclusive   INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_sale_lines_sale ON sale_lines(sale_id);
CREATE INDEX IF NOT EXISTS ix_sale_lines_product ON sale_lines(product_id);

CREATE TABLE IF NOT EXISTS payments (
    id             TEXT PRIMARY KEY,
    sale_id        TEXT NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
    method         TEXT NOT NULL,
    amount_cents   INTEGER NOT NULL DEFAULT 0,
    tendered_cents INTEGER NOT NULL DEFAULT 0,
    reference      TEXT,
    created_at     TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_payments_sale ON payments(sale_id);
CREATE INDEX IF NOT EXISTS ix_payments_method ON payments(method);

CREATE TABLE IF NOT EXISTS invoices (
    id              TEXT PRIMARY KEY,
    sale_id         TEXT NOT NULL UNIQUE REFERENCES sales(id) ON DELETE CASCADE,
    number          TEXT NOT NULL UNIQUE,
    sequence        INTEGER NOT NULL,
    prefix          TEXT NOT NULL DEFAULT '',
    issued_at       TEXT NOT NULL,
    customer_name   TEXT,
    customer_tax_id TEXT,
    total_cents     INTEGER NOT NULL DEFAULT 0,
    fiscal_data     TEXT,
    status          TEXT NOT NULL DEFAULT 'Issued'
);
CREATE INDEX IF NOT EXISTS ix_invoices_issued ON invoices(issued_at);

CREATE TABLE IF NOT EXISTS cash_movements (
    id           TEXT PRIMARY KEY,
    session_id   TEXT NOT NULL REFERENCES cash_sessions(id) ON DELETE CASCADE,
    type         TEXT NOT NULL,
    amount_cents INTEGER NOT NULL,
    reason       TEXT NOT NULL DEFAULT '',
    user_id      TEXT,
    user_name    TEXT NOT NULL DEFAULT '',
    created_at   TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_cash_movements_session ON cash_movements(session_id);

CREATE TABLE IF NOT EXISTS stock_movements (
    id            TEXT PRIMARY KEY,
    product_id    TEXT NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    product_name  TEXT NOT NULL DEFAULT '',
    type          TEXT NOT NULL,
    quantity_milli INTEGER NOT NULL,
    reference_id  TEXT,
    reference     TEXT,
    user_id       TEXT,
    user_name     TEXT,
    note          TEXT,
    created_at    TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_stock_movements_product ON stock_movements(product_id);
CREATE INDEX IF NOT EXISTS ix_stock_movements_created ON stock_movements(created_at);

CREATE TABLE IF NOT EXISTS purchase_orders (
    id            TEXT PRIMARY KEY,
    number        TEXT NOT NULL UNIQUE,
    supplier_id   TEXT,
    supplier_name TEXT NOT NULL DEFAULT '',
    status        TEXT NOT NULL,
    ordered_at    TEXT NOT NULL,
    received_at   TEXT,
    total_cents   INTEGER NOT NULL DEFAULT 0,
    notes         TEXT
);
CREATE INDEX IF NOT EXISTS ix_purchase_orders_date ON purchase_orders(ordered_at);

CREATE TABLE IF NOT EXISTS purchase_order_lines (
    id                   TEXT PRIMARY KEY,
    purchase_order_id    TEXT NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE,
    product_id           TEXT NOT NULL,
    product_name         TEXT NOT NULL DEFAULT '',
    sku                  TEXT NOT NULL DEFAULT '',
    quantity_milli       INTEGER NOT NULL DEFAULT 0,
    unit_cost_cents      INTEGER NOT NULL DEFAULT 0,
    received_quantity_milli INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_purchase_lines_order ON purchase_order_lines(purchase_order_id);

CREATE TABLE IF NOT EXISTS stock_counts (
    id         TEXT PRIMARY KEY,
    number     INTEGER NOT NULL,
    status     TEXT NOT NULL,
    created_at TEXT NOT NULL,
    closed_at  TEXT,
    user_id    TEXT,
    user_name  TEXT NOT NULL DEFAULT '',
    notes      TEXT
);
CREATE INDEX IF NOT EXISTS ix_stock_counts_status ON stock_counts(status);

CREATE TABLE IF NOT EXISTS stock_count_lines (
    id                  TEXT PRIMARY KEY,
    stock_count_id      TEXT NOT NULL REFERENCES stock_counts(id) ON DELETE CASCADE,
    product_id          TEXT NOT NULL,
    product_name        TEXT NOT NULL DEFAULT '',
    sku                 TEXT NOT NULL DEFAULT '',
    system_quantity_milli INTEGER NOT NULL DEFAULT 0,
    counted_quantity_milli INTEGER NOT NULL DEFAULT 0,
    UNIQUE(stock_count_id, product_id)
);

CREATE TABLE IF NOT EXISTS audit_log (
    id          TEXT PRIMARY KEY,
    user_id     TEXT,
    user_name   TEXT NOT NULL DEFAULT '',
    action      TEXT NOT NULL,
    entity_type TEXT NOT NULL DEFAULT '',
    entity_id   TEXT,
    details     TEXT,
    created_at  TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_created ON audit_log(created_at);
CREATE INDEX IF NOT EXISTS ix_audit_action ON audit_log(action);
