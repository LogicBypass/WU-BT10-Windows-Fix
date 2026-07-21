# Support-link configuration

The application has no payment SDK and never handles card data. An optional
button opens a configured HTTPS support page in the user's default browser.

Set the GitHub Actions repository variable `SUPPORT_URL` to one of:

- a GitHub Sponsors profile;
- a Ko-fi page; or
- a Buy Me a Coffee page.

Configure the default €5 amount and custom-amount option with the selected
provider. Those settings belong at the payment provider, not inside this
administrator-capable desktop utility.

The button is omitted when `SUPPORT_URL` is empty or invalid. This keeps forks
from accidentally sending money to the original maintainer.

## Why Paddle is not used for tips

Paddle is designed as a merchant of record for real software and digital
products. Its acceptable-use rules prohibit donation, crowdfunding, and
sponsorship payments where no bona fide product or service is sold. Do not label
a donation as a software sale merely to route it through Paddle.

If a future paid edition offers genuine licensed functionality, integrate it as
a separate product with clear pricing, fulfillment, refund, tax, and privacy
documentation. Keep the repair itself functional without a purchase.
