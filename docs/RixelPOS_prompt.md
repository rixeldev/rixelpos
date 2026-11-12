# Prompt: Build **RixelPOS** — A Cross-Platform Billing & Point-of-Sale System by RixelDev

## 1. Role

You are a senior software architect and full-stack engineer with deep experience in desktop applications, point-of-sale (POS) systems, hardware integration (barcode scanners, receipt printers, cash drawers) and UX design for non-technical users.

## 2. Goal

Design and build **RixelPOS**, a complete, production-ready billing and point-of-sale system for small and medium businesses (shops, minimarkets, pharmacies, restaurants, hardware stores). It is a product of my digital brand, **RixelDev**.

The system must be:

- **Extremely intuitive**: a cashier with zero training should be able to make a sale in under 2 minutes.
- **Fast**: a sale with a scanner must feel instant, no lag when scanning items.
- **Reliable**: it must keep working with no internet connection (offline-first).
- **A real installable desktop application, NOT a web app**: it is installed with a normal installer, launches from the desktop / start menu / application launcher, and runs in its own native window. No browser, no hosted website, no PWA, no local web server that the user has to open in a browser, and no dependency on an internet connection or external servers to work.
- **Cross-platform**: runs natively on **Linux (CachyOS / Arch-based)**, **Windows 10** and **Windows 11**, from a single codebase.

> Branding: the product name is **RixelPOS** (feel free to propose 2-3 alternative names that fit the RixelDev brand, e.g. RixelBill, RixelCaja, but use RixelPOS by default). Keep the name configurable in one place.

## 3. Technology Choice

You may choose **any programming language and framework**, but you must justify the choice in a short section before writing code. Evaluate at least these options against the requirements (cross-platform, hardware access, performance, packaging, maintainability):

1. **C# / .NET with Avalonia UI** (native, cross-platform, excellent tooling).
2. **Python with PySide6 (Qt 6 / QML)** (fast to develop, mature Qt widgets, good hardware libraries).
3. **C++ with Qt 6** (maximum performance and native feel).
4. **Rust with Slint** (or iced / egui) for a lightweight native app.
5. **Flutter Desktop (Dart)** or **Go with Fyne** as additional candidates.

Rules for the choice:

- **It must be a native desktop application with a native UI toolkit.** Web-based solutions are explicitly excluded: no web apps, no PWAs, no hosted or browser-based UI, no Electron, and no webview-wrapped websites (e.g. Tauri/Electron with a React/Vue frontend). Do not propose them as the final choice.
- The same code must build for Linux and Windows without platform-specific rewrites.
- Platform-specific code (printers, serial/USB access) must be isolated behind interfaces/adapters.
- Prefer technologies with good, maintained libraries for ESC/POS printing and USB/serial devices.
- The final result must be distributable as an easy installer: `.exe`/`.msi` for Windows and `AppImage` / `.deb` / `PKGBUILD` (AUR-ready) for Linux/CachyOS.

## 4. Core Features

### 4.1 Sales / Checkout screen (the heart of the app)

- One-screen checkout: product search bar always focused, big cart list, big total, big "Charge" button.
- **Barcode scanner support**: scanning adds the product instantly; scanning the same product again increments the quantity.
- Manual search by name, SKU or code with live autocomplete.
- Quick-access product buttons (favorites / categories) for items without barcodes (bread, produce, etc.).
- Edit quantity, apply per-item or whole-sale discounts (percent or fixed amount), remove items.
- Hold / resume sales (park a sale and serve another customer).
- Multiple payment methods: cash (with change calculation), card, transfer, mixed/split payments, store credit.
- Keyboard-first workflow with shortcuts (F-keys, Enter to charge, Esc to cancel) **and** a fully touch-friendly layout.

### 4.2 Invoices & Receipts

- Generate receipts/invoices automatically after each sale.
- Support **thermal receipt printers (ESC/POS)** at 58 mm and 80 mm widths, plus standard A4/Letter PDF invoices.
- Customizable template: business logo, name, address, tax ID, footer message, QR code.
- Sequential invoice numbering, configurable prefix (e.g. `A-000123`).
- Reprint any past receipt; export invoices to PDF.
- Support tax configuration (e.g. VAT/ITBIS/IVA), with tax-inclusive and tax-exclusive pricing.
- Design the fiscal layer so it is **pluggable** (country-specific fiscal rules, such as electronic invoicing, can be added later without rewriting the core). Make it ready for the Dominican Republic (NCF / e-CF) as an optional module, but not mandatory.

### 4.3 Hardware Integration

Create a dedicated **hardware abstraction layer** with clear interfaces and mock implementations for testing without devices.

- **Barcode scanners**: USB keyboard-wedge mode (most common), USB HID, and serial (COM / `/dev/ttyUSB*`). Handle EAN-13, EAN-8, UPC-A, Code 128, Code 39, QR.
- **Receipt printers**: ESC/POS over USB, Serial, Bluetooth and Network (TCP 9100). Brands to consider: Epson, Xprinter, Bixolon, Star, generic Chinese ESC/POS printers.
- **Cash drawer** opening via printer kick pulse.
- **Label printing** for products (barcode labels) as an optional module.
- **Customer display / pole display** (optional).
- Printer auto-detection, a "Test print" button, and clear, human-readable error messages when a device is disconnected (never raw stack traces).
- Document Linux specifics (udev rules, user permissions for USB/serial, CUPS vs raw printing) and Windows specifics (drivers, raw printing via spooler, Zadig/WinUSB if needed).

### 4.4 Inventory

- Products with: name, SKU, barcode(s), category, cost, price, tax, stock, minimum stock, unit, image (optional).
- Multiple barcodes per product; variants (size/color) as an optional feature.
- Automatic stock deduction on sale, manual adjustments, purchase/stock-in entries.
- Low-stock alerts on the dashboard.
- Bulk import/export via CSV/Excel.

**Scan-to-add workflow (must be a first-class feature):**

- A dedicated **"Inventory mode"** where the scanner is always listening: I scan **any barcode** (EAN-13, EAN-8, UPC-A, Code 128, Code 39, QR) and the app reacts instantly.
- If the barcode **already exists**: show the product and ask for the quantity to add to stock (default +1; scanning again increments the counter), so I can receive merchandise just by scanning.
- If the barcode **does not exist**: open a quick "New product" form with the barcode pre-filled and the cursor on the name field. Only name, price and quantity are required; everything else (category, cost, tax, minimum stock, photo) is optional and can be completed later.
- **Optional online lookup**: when internet is available, try to auto-fill name, brand and image from a public product database (e.g. Open Food Facts / UPC database), always editable by me and never blocking if offline.
- **Batch receiving mode**: scan many items in a row and confirm all stock changes at once, with a summary screen before saving.
- **Physical stock count mode**: scan items to count what is on the shelf, compare against system stock and generate an adjustment report with differences.
- Support generating **my own internal barcodes** (Code 128 / EAN-13 with a store prefix) for products without one, plus printing the label on a thermal or label printer.
- Audible and visual feedback on every scan (different sounds for "found", "new product" and "error"), and protection against duplicate scans (configurable debounce).
- Works with any scanner in keyboard-wedge, HID or serial mode, with no special configuration needed.

### 4.5 Customers & Suppliers

- Simple customer records (name, phone, tax ID, address), optional on each sale.
- Customer purchase history and optional credit/accounts receivable.
- Basic supplier records and purchase orders.

### 4.6 Cash Register Management

- Open and close the cash register (shift) with opening balance.
- Cash in / cash out movements with reasons.
- End-of-day report (Z report) with totals by payment method.

### 4.7 Reports & Dashboard

- Home dashboard: today's sales, top products, low stock, recent invoices.
- Reports: sales by day/week/month, by product, by category, by cashier, by payment method, profit margin.
- Export to PDF and CSV/Excel.

### 4.8 Users, Roles & Security

- Roles: **Admin**, **Manager**, **Cashier** with clear permissions.
- Quick cashier login with PIN; password-based login for admin.
- Audit log (who voided a sale, changed a price, opened the drawer, etc.).
- Passwords hashed with Argon2 or bcrypt; never stored in plain text.
- Manager approval required for voids, big discounts and refunds.

### 4.9 Settings & Backup

- First-run **setup wizard** (business info, currency, tax, printer detection, scanner test, first admin user).
- Automatic local backups with one-click restore; optional export to a folder or external drive.
- Language support (i18n): **Spanish and English** at minimum, easy to add more.
- Light and dark themes.

## 5. UX / UI Requirements (very important)

- Clean, modern, minimal interface with a consistent design system (spacing, colors, typography). Use the RixelDev feel: modern, techy, friendly.
- Big, readable buttons and text; high contrast; good for touchscreens and for long shifts.
- Never more than 2 clicks to reach any common action.
- Clear visual feedback: sounds/visual cue on successful scan, color-coded stock warnings, confirmation toasts.
- Helpful empty states and inline hints instead of manuals.
- Friendly error messages that explain **what happened and how to fix it**.
- Fully usable with keyboard only, mouse only, or touch only.
- Accessible: scalable font sizes, color-blind-safe palette.
- Provide a short interactive onboarding tour on first launch.

## 6. Architecture & Data

- **Offline-first** with a local embedded database: **SQLite** (with migrations, e.g. via Diesel, SQLx, SQLAlchemy/Alembic, EF Core or the native SQLite bindings of the chosen stack).
- Clean architecture: `domain` / `application` / `infrastructure` / `ui`, with the hardware layer behind interfaces.
- Optional future sync module (cloud or LAN multi-terminal) — design for it, but implement it only if time allows. Describe how it would work (e.g. one main terminal acting as local server).
- All money handled with integer cents or a decimal type — **never floating point**.
- Validate all inputs (e.g. Zod / Pydantic / equivalent).
- Structured logging to a local file with rotation.

## 7. Quality Requirements

- Unit tests for business logic (totals, taxes, discounts, stock, invoice numbering).
- Integration tests for the database layer.
- Hardware layer tested with mocks/simulators.
- Linting and formatting configured.
- CI pipeline (GitHub Actions) that builds and tests for **Linux and Windows**.
- No hardcoded secrets; configuration via files/env.

## 8. Packaging & Distribution

- Windows 10/11: signed-ready installer (`.msi` or NSIS `.exe`), plus portable build.
- Linux (CachyOS / Arch): `AppImage` and a ready-to-use `PKGBUILD`; also `.deb` for broader compatibility.
- Auto-update mechanism (optional, but describe the approach).
- Provide the exact build commands for each platform.

## 9. Deliverables (in this order)

1. **Technology decision** with a short comparison and final justification.
2. **Project structure** (full folder tree).
3. **Database schema** (tables, relations, indexes) with migrations.
4. **Hardware abstraction layer** (scanner, printer, cash drawer) with real and mock implementations.
5. **Core business logic** (sales, taxes, discounts, stock, invoices, cash register).
6. **UI**: all main screens (setup wizard, login, checkout, inventory, customers, reports, settings), following the UX rules above.
7. **Printing templates** for 58 mm and 80 mm receipts and A4 PDF invoice.
8. **Tests** and CI configuration.
9. **Packaging scripts** for Windows and Linux/CachyOS.
10. **README.md** with: installation, hardware setup (Linux and Windows), usage guide, troubleshooting, and screenshots/mockups description.

## 10. Working Method

- Start with a short plan and ask me **at most 5 clarifying questions** only if something critical is truly ambiguous; otherwise make reasonable assumptions and state them clearly.
- Build the project **incrementally**: deliver a working MVP first (checkout + scanner + receipt printing + inventory), then add the remaining modules.
- Provide complete, runnable code — no pseudo-code or "TODO" placeholders in critical paths.
- After each major step, tell me how to run and test it on **CachyOS (Linux)** and on **Windows 10/11**.
- Explain important decisions briefly; keep the explanations practical.

## 11. Definition of Done

- A cashier can scan products, take payment, print a receipt and close the day without reading any documentation.
- The app installs with a standard installer, appears in the start menu / app launcher with its own icon, and opens in its own native window, with no browser and no server involved.
- The app runs and prints correctly on CachyOS and on Windows 10/11.
- The app works fully offline and never loses a sale, even after a sudden power cut (use database transactions and write-ahead logging).
- Code is clean, tested, documented and ready to ship under the **RixelDev** brand.

---

*Start now with step 1 (technology decision) and step 2 (project structure).*
