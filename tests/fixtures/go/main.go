package main

import "fmt"

func main() {
    h := NewOrderHandler("ORD-")
    fmt.Println(h.Prefix)
}
