<?php

namespace App\Services;

use App\Repositories\OrderRepository;
use App\Repositories\IOrderRepository;

#[Route('/api/orders')]
class OrderService
{
    private IOrderRepository $repository;

    public function __construct(OrderRepository $repository)
    {
        $this->repository = $repository;
    }

    public function getOrder(int $id): ?array
    {
        return $this->repository->find($id);
    }
}
