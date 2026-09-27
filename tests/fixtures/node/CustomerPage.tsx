import React from 'react';
import { CustomerWidget } from './CustomerWidget';

export const CustomerPage: React.FC = () => {
  return (
    <div>
      <h1>Customer Overview</h1>
      <CustomerWidget id={101} />
    </div>
  );
};
