const inrFormat = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** Rupees with Indian digit grouping, for example ₹12,34,567.00. */
export function formatInr(value: number): string {
  return inrFormat.format(value);
}
