<?php

namespace App\Repositories;

class OrderRepository implements IOrderRepository
{
    public function find(int $id): ?array
    {
        return ['id' => $id, 'total' => 120.00];
    }
}
