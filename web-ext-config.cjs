// Keep the companion app and build artifacts out of the packaged extension and
// out of `web-ext lint`. Only the extension's own files should ship in the xpi.
module.exports = {
  ignoreFiles: [
    "local-app",
    "web-ext-artifacts",
    "*.md",
    ".gitignore",
    "web-ext-config.cjs",
  ],
};
