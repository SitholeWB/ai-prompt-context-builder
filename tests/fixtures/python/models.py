"""Customer data models."""
from dataclasses import dataclass

@dataclass
class Customer:
    id: int
    name: str
