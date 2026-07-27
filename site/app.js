const externalLinks = document.querySelectorAll('a[href^="https://"]');

for (const link of externalLinks) {
  link.rel = "noopener noreferrer";
}
