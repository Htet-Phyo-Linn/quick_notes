const http = require('http');
const fs = require('fs');
const path = require('path');

const assetsDir = path.resolve(__dirname, '../src/QuickNote.Avalonia/QuickNote.Avalonia/Assets/milkdown');
const server = http.createServer((req, res) => {
    let reqUrl = req.url.split('?')[0];
    let filePath = path.join(assetsDir, reqUrl === '/' ? 'index.html' : reqUrl);
    if (fs.existsSync(filePath)) {
        if (filePath.endsWith('.html')) res.setHeader('Content-Type', 'text/html');
        else if (filePath.endsWith('.js')) res.setHeader('Content-Type', 'application/javascript');
        else if (filePath.endsWith('.css')) res.setHeader('Content-Type', 'text/css');
        res.end(fs.readFileSync(filePath));
    } else {
        res.statusCode = 404;
        res.end('Not found');
    }
});

server.listen(9880, async () => {
    const { spawn } = require('child_process');
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const edge = spawn(edgePath, [
        '--remote-debugging-port=9226',
        '--headless=new',
        '--no-sandbox',
        '--disable-gpu',
        '--user-data-dir=' + path.resolve('temp_edge_profile_paste4'),
        'http://localhost:9880/index.html'
    ]);

    await new Promise(r => setTimeout(r, 2000));

    try {
        const res = await fetch('http://localhost:9226/json');
        const targets = await res.json();
        const pageTarget = targets.find(t => t.url.includes('9880'));
        const ws = new WebSocket(pageTarget.webSocketDebuggerUrl);

        let id = 1;
        const send = (method, params = {}) => new Promise((resolve, reject) => {
            const reqId = id++;
            const timer = setTimeout(() => reject(new Error(`Timeout on ${method}`)), 5000);
            const handler = (event) => {
                const data = JSON.parse(event.data);
                if (data.id === reqId) {
                    clearTimeout(timer);
                    ws.removeEventListener('message', handler);
                    resolve(data);
                }
            };
            ws.addEventListener('message', handler);
            ws.send(JSON.stringify({ id: reqId, method, params }));
        });

        ws.onopen = async () => {
            await send('Runtime.enable');
            await send('Page.enable');

            await new Promise(r => setTimeout(r, 1000));

            const mdText = "# Quick Note Bugs\\n\\n* [x] Right Click feature didn't work\\n* [x] Spell check bugs\\n* [ ] Table Heading change bugs";

            const result = await send('Runtime.evaluate', {
                expression: `
                    (() => {
                        try {
                            const view = window.getEditorView();
                            const parser = window.__getParser();
                            if (!parser) return { error: "No parser" };
                            const parsedDoc = parser(${JSON.stringify(mdText)});
                            return {
                                parsedType: parsedDoc.type.name,
                                childCount: parsedDoc.childCount,
                                children: Array.from({ length: parsedDoc.childCount }, (_, i) => parsedDoc.child(i).type.name)
                            };
                        } catch(e) {
                            return { error: e.stack };
                        }
                    })()
                `,
                returnByValue: true
            });

            console.log('Parser test result:', result.result.result.value);

            edge.kill();
            server.close();
            process.exit(0);
        };
    } catch(err) {
        console.error('Fatal:', err);
        edge.kill();
        server.close();
        process.exit(1);
    }
});
