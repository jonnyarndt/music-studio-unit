const net = require('net');
const readline = require('readline');

const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout
});

let host = '';
const port = 53001;
let isConnected = false;
let isSending = false;
let client;

const generateRandomBytes = (length) => {
    const bytes = [];
    for (let i = 0; i < length; i++) {
        bytes.push(Math.floor(Math.random() * 256));
    }
    return Buffer.from(bytes);
};

const connectToServer = () => {
    client = net.createConnection({ host, port }, () => {
        isConnected = true;
        console.log('Connected to server');
        if (isSending) {
            sendRandomString(client);
        }
    });

    client.on('data', (data) => {
        console.log('Received:', data.toString());
    });

    client.on('end', () => {
        isConnected = false;
        console.log('Disconnected from server');
    });
};

const sendRandomString = () => {
    const header = Buffer.from([0x02, 0x09]);
    const footer = Buffer.from([0x03, 0x0D]);
    const randomBytes = generateRandomBytes(26);
    const randomString = Buffer.concat([header, randomBytes, footer]).toString('hex');
    client.write(randomString);
    console.log('Sent:', randomString);
};

rl.question('Enter the IP address or hostname to connect to: ', (inputHost) => {
    host = inputHost;
    rl.close();

    rl.on('line', (input) => {
        if (input === 'start') {
            if (!isConnected) {
                connectToServer();
            }
            isSending = true;
            sendRandomString();
        } else if (input === 'stop') {
            isSending = false;
        } else if (input === 'exit') {
            if (client) {
                client.end();
            }
            process.exit(0);
        }
    });

    setInterval(() => {
        if (isConnected && isSending) {
            sendRandomString();
        }
    }, 60000);
});
