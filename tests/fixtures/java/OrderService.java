package com.example.service;

public class OrderService {
    private final IOrderRepository repository;

    public OrderService(IOrderRepository repository) {
        this.repository = repository;
    }

    public String getOrder(String id) {
        return repository.findById(id);
    }
}
