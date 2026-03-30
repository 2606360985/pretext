const server = Bun.serve({
  port: 3008,
  async fetch(req) {
    const url = new URL(req.url);
    let path = url.pathname;
    path = path.replace(/\\/g, "/");
    if (path.endsWith("/")) path = path.slice(0, -1);
    const htmlPath = "pages" + path + ".html";
    const fs = require("fs");
    if (fs.existsSync(htmlPath)) {
      return new Response(Bun.file(htmlPath));
    }
    const indexPath = "pages" + path + "/index.html";
    if (fs.existsSync(indexPath)) {
      return new Response(Bun.file(indexPath));
    }
    return new Response("Not found", { status: 404 });
  }
});
console.log("Server at http://localhost:" + server.port);