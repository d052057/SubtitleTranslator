const { env } = require('process');

const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
    env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7064';

const PROXY_CONFIG = [
  // Add proxy entries for your API routes here, e.g.:
  // { context: ["/api"], target, secure: false }
]

module.exports = PROXY_CONFIG;
