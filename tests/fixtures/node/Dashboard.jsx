import React from 'react';
import { MetricCard } from './MetricCard';

export function Dashboard() {
  return (
    <div className="dashboard-container">
      <h2>Executive Dashboard</h2>
      <MetricCard title="Active Users" value="12,540" />
    </div>
  );
}
