package com.example.service

import com.example.repository.OrderRepository

class OrderService(private val repo: OrderRepository) : OrderRepository {
    override fun findById(id: Long): String? {
        return repo.findById(id)
    }

    fun calculateTotal(price: Double): Double {
        return price * 1.15
    }
}
