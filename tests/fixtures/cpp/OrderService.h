#ifndef ORDER_SERVICE_H
#define ORDER_SERVICE_H

#include "IOrderService.h"

class OrderService : public IOrderService {
public:
    double CalculateTotal(double subtotal) override;
};

#endif
