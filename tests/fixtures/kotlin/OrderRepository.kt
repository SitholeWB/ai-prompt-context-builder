package com.example.repository

interface OrderRepository {
    fun findById(id: Long): String?
}
