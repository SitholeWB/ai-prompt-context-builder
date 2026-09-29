declare global {
  interface String {
    toSlug(): string;
  }
}

String.prototype.toSlug = function(this: string): string {
  return this.toLowerCase().replace(/\s+/g, '-');
};
