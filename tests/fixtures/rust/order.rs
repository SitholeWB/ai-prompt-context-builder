// Rust Order Fixture
use crate::repository::OrderRepository;
use crate::repository::SqliteOrderRepo;

pub struct Order {
    pub id: u64,
    pub customer_id: String,
    pub amount: f64,
}

impl OrderRepository for SqliteOrderRepo {
    fn find_by_id(&self, id: u64) -> Option<Order> {
        Some(Order { id, customer_id: "cust-1".to_string(), amount: 99.5 })
    }
}
