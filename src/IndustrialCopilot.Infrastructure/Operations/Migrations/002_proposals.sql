CREATE TABLE IF NOT EXISTS operations.work_order_proposals (
    id uuid PRIMARY KEY REFERENCES operations.work_orders(id),
    payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object')
);
