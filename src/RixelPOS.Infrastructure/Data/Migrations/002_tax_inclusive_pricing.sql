-- Version 2: sale prices already include tax.
--
-- The price entered for a product is the final price the customer pays. The tax is
-- extracted from that amount instead of being added on top of the total. This applies
-- to existing catalogs so the behaviour is consistent after updating.
--
-- Historical sales are intentionally NOT modified: sale_lines keeps the tax_inclusive
-- snapshot that was used when the sale was completed.

UPDATE products SET tax_inclusive = 1 WHERE tax_inclusive = 0;
UPDATE tax_rates SET is_inclusive = 1 WHERE is_inclusive = 0;
UPDATE settings SET value = 'True' WHERE key = 'tax.inclusive';
