package main

type OrderHandler struct {
    Prefix string
}

func NewOrderHandler(prefix string) *OrderHandler {
    return &OrderHandler{Prefix: prefix}
}
