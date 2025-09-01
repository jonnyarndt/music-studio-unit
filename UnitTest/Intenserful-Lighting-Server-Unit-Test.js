const net = require('net');

const port = 53002;
const allowedClientMap = new Map();

const server = net.createServer((client) => {
  const clientAddress = client.remoteAddress;
  
  if (allowedClientMap.has(clientAddress)) {
    client.end('NACK\r');
    console.log(`Invalid packet from ${clientAddress}`);
    return;
  }
  
  allowedClientMap.set(clientAddress, true);

  console.log(`Accepted connection from ${clientAddress}`);

  let receivedData = Buffer.alloc(0);

  client.on('data', (data) => {
    receivedData = Buffer.concat([receivedData, data]);

    while (receivedData.length >= 15) {
      if (
        receivedData.readUInt32BE(0) === 0x414A1002 &&
        receivedData.toString('ascii', 4, 13).match(/^#[A-Za-z0-9]{8}$/) &&
        receivedData[13] === 0x0D
      ) {
        client.write('OK\r');
        console.log(`Valid packet from ${clientAddress}`);
        receivedData = receivedData.slice(15);
      } else {
        client.write('NACK\r');
        console.log(`Invalid packet from ${clientAddress}`);
        client.end();
        return;
      }
    }
  });

  client.on('end', () => {
    allowedClientMap.delete(clientAddress);
    console.log(`Connection closed with ${clientAddress}`);
  });
});

server.on('error', (err) => {
  console.error('Server error:', err);
});

server.listen(port, () => {
  console.log(`Server listening on port ${port}`);
});
