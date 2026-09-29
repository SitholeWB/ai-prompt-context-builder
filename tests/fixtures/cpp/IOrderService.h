#ifndef I_ORDER_SERVICE_H
#define I_ORDER_SERVICE_H

class IOrderService {
public:
    virtual ~IOrderService() = default;
    virtual double CalculateTotal(double subtotal) = 0;
};

#endif
