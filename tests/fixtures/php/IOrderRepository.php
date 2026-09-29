<?php

namespace App\Repositories;

interface IOrderRepository
{
    public function find(int $id): ?array;
}
