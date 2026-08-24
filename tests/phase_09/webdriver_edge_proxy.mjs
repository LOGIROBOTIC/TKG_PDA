import http from 'node:http';

const listenPort = Number.parseInt(process.env.WEBDRIVER_PROXY_PORT ?? '4444', 10);
const targetPort = Number.parseInt(process.env.WEBDRIVER_TARGET_PORT ?? '4445', 10);

function edgeCapabilities(capabilities) {
  if (!capabilities || typeof capabilities !== 'object') {
    return capabilities;
  }

  const converted = { ...capabilities, browserName: 'MicrosoftEdge' };
  if (converted['goog:chromeOptions']) {
    converted['ms:edgeOptions'] = converted['goog:chromeOptions'];
    delete converted['goog:chromeOptions'];
  }
  if (converted['goog:loggingPrefs']) {
    converted['ms:loggingPrefs'] = converted['goog:loggingPrefs'];
    delete converted['goog:loggingPrefs'];
  }
  return converted;
}

function translateSessionBody(buffer) {
  if (buffer.length === 0) {
    return buffer;
  }

  const document = JSON.parse(buffer.toString('utf8'));
  const capabilities = document.capabilities;
  if (capabilities?.alwaysMatch) {
    capabilities.alwaysMatch = edgeCapabilities(capabilities.alwaysMatch);
  }
  if (Array.isArray(capabilities?.firstMatch)) {
    capabilities.firstMatch = capabilities.firstMatch.map(edgeCapabilities);
  }
  if (document.desiredCapabilities) {
    document.desiredCapabilities = edgeCapabilities(document.desiredCapabilities);
  }
  return Buffer.from(JSON.stringify(document), 'utf8');
}

const server = http.createServer((request, response) => {
  const chunks = [];
  request.on('data', (chunk) => chunks.push(chunk));
  request.on('end', () => {
    try {
      const incoming = Buffer.concat(chunks);
      const body =
        request.method === 'POST' && /\/session\/?$/.test(request.url ?? '')
          ? translateSessionBody(incoming)
          : incoming;
      const headers = { ...request.headers, host: `127.0.0.1:${targetPort}` };
      headers['content-length'] = String(body.length);

      const upstream = http.request(
        {
          hostname: '127.0.0.1',
          port: targetPort,
          path: request.url,
          method: request.method,
          headers,
        },
        (upstreamResponse) => {
          response.writeHead(
            upstreamResponse.statusCode ?? 502,
            upstreamResponse.headers,
          );
          upstreamResponse.pipe(response);
        },
      );
      upstream.on('error', (error) => {
        response.writeHead(502, { 'content-type': 'application/json' });
        response.end(
          JSON.stringify({ value: { error: 'unknown error', message: error.message } }),
        );
      });
      upstream.end(body);
    } catch (error) {
      response.writeHead(400, { 'content-type': 'application/json' });
      response.end(
        JSON.stringify({ value: { error: 'invalid argument', message: error.message } }),
      );
    }
  });
});

server.listen(listenPort, '127.0.0.1', () => {
  process.stdout.write(`Edge WebDriver capability proxy listening on ${listenPort}\n`);
});
