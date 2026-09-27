import React from 'react';

export interface CustomerWidgetProps {
  id: number;
}

export const CustomerWidget: React.FC<CustomerWidgetProps> = ({ id }) => {
  return <div>Customer ID: {id}</div>;
};
