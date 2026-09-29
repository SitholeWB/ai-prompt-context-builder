// Rust Test Fixtures
pub trait OrderRepository {
    fn find_by_id(&self, id: u64) -> Option<Order>;
}

pub struct SqliteOrderRepo;
