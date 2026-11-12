-- Version 3: no implicit tax rate.
--
-- RixelPOS no longer assumes an 18% rate. Products created before this migration that
-- still carry the old default are set to 0 so the amount charged is exactly the price
-- the user entered. Prices remain tax-inclusive; a business can opt into a rate later.

UPDATE products SET tax_inclusive = 1, tax_rate_bp = 0;
UPDATE tax_rates SET rate_bp = 0, is_inclusive = 1 WHERE is_default = 1;
UPDATE settings SET value = '0' WHERE key = 'tax.default_bp';
UPDATE settings SET value = 'True' WHERE key = 'tax.inclusive';
