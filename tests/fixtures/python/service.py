"""Customer service module."""
from .models import Customer

# Business logic service
class CustomerService:
    """Service handling customer queries."""
    def get_customer(self, customer_id: int) -> Customer:
        # Retrieve customer instance
        return Customer(id=customer_id, name="Alice")
