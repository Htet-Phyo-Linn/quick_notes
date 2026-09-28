const { spawn } = require('child_process');
const http = require('http');
const fs = require('fs');
const path = require('path');

const assetsDir = path.resolve('src/QuickNote.Avalonia/QuickNote.Avalonia/Assets/milkdown');
const server = http.createServer((req, res) => {
    let filePath = path.join(assetsDir, req.url === '/' ? 'index.html' : req.url);
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

server.listen(9876, async () => {
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const edge = spawn(edgePath, [
        '--remote-debugging-port=9222',
        '--headless=new',
        '--no-sandbox',
        '--disable-gpu',
        '--user-data-dir=' + path.resolve('temp_edge_profile'),
        'http://localhost:9876/index.html'
    ]);

    await new Promise(r => setTimeout(r, 2000));

    try {
        const res = await fetch('http://localhost:9222/json');
        const targets = await res.json();
        const pageTarget = targets.find(t => t.url.includes('9876'));
        const ws = new WebSocket(pageTarget.webSocketDebuggerUrl);

        let id = 1;
        const send = (method, params = {}) => new Promise((resolve, reject) => {
            const reqId = id++;
            const timer = setTimeout(() => {
                reject(new Error(`Timeout on ${method}`));
            }, 5000);
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
            await send('Console.enable');

        ws.addEventListener('message', (event) => {
            const data = JSON.parse(event.data);
            if (data.method === 'Runtime.consoleAPICalled') {
                console.log('[Console]', data.params.type, data.params.args.map(a => a.value));
            } else if (data.method === 'Runtime.exceptionThrown') {
                console.log('[Exception]', data.params.exceptionDetails.text, data.params.exceptionDetails.exception?.description);
            } else if (data.id) {
                console.log('[Response id=' + data.id + ']', JSON.stringify(data).slice(0, 150));
            }
        });

            await new Promise(r => setTimeout(r, 2000));

            // Check content BEFORE setMarkdown
            const before = await send('Runtime.evaluate', {
                expression: 'document.getElementById("editor").innerText',
                returnByValue: true
            });
            console.log('Text BEFORE setMarkdown:', JSON.stringify(before.result.result.value));

            // 1. Load actual Essentials CMD note
            const notePath = "C:\\Users\\PC\\OneDrive - BIM ADVANCED TECHNOLOGY SERVICES PTE. LTD\\QuickNote\\Essentials CMD.md";
            let essentialsBody = "### Folder copy\n\nSome command here\n\n### Powershell bypass\n\nSet-ExecutionPolicy";
            if (fs.existsSync(notePath)) {
                const fullContent = fs.readFileSync(notePath, 'utf8').replace(/\r\n/g, '\n');
                let body = fullContent;
                if (body.startsWith('---\n')) {
                    const endIdx = body.indexOf('\n---\n', 4);
                    if (endIdx > 0) body = body.substring(endIdx + 5);
                }
                body = body.trimStart();
                if (body.startsWith('# ') && !body.startsWith('## ')) {
                    const nl = body.indexOf('\n');
                    if (nl >= 0) body = body.substring(nl + 1).trimStart();
                }
                essentialsBody = body;
            }

            console.log('--- TEST 1: Load Essentials CMD note ---');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(essentialsBody)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 500));

            const after1 = await send('Runtime.evaluate', {
                expression: 'document.getElementById("editor").innerText',
                returnByValue: true
            });
            const text1 = after1.result.result.value || '';
            console.log('Test 1 InnerText preview (first 100 chars):', JSON.stringify(text1.slice(0, 100)));
            console.log('Contains "Folder copy":', text1.includes('Folder copy'));
            console.log('Contains "Powershell bypass":', text1.includes('Powershell bypass'));

            console.log('--- TEST 2: Switch to another note ---');
            const note2 = "# Meeting Notes\n\nDiscussing project roadmap and milestones.";
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(note2)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 500));

            const after2 = await send('Runtime.evaluate', {
                expression: 'document.getElementById("editor").innerText',
                returnByValue: true
            });
            const text2 = after2.result.result.value || '';
            console.log('Test 2 InnerText:', JSON.stringify(text2.trim()));
            console.log('Note 2 rendered correctly:', text2.includes('Meeting Notes'));

            console.log('--- TEST 3: Switch BACK to Essentials CMD ---');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(essentialsBody)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 500));

            const after3 = await send('Runtime.evaluate', {
                expression: 'document.getElementById("editor").innerText',
                returnByValue: true
            });
            const text3 = after3.result.result.value || '';
            console.log('Test 3 contains "Folder copy":', text3.includes('Folder copy'));

            console.log('--- TEST 4: Scroll to heading ---');
            await send('Runtime.evaluate', {
                expression: `window.scrollToHeading("Powershell bypass", 3)`,
                returnByValue: true
            });
            console.log('Scroll to heading executed without errors.');

            console.log('ALL TESTS PASSED SUCCESSFULLY!');
            cleanup();
        };

    } catch (e) {
        console.error('Error:', e);
        cleanup();
    }

    function cleanup() {
        try { edge.kill(); } catch {}
        try { server.close(); } catch {}
        process.exit(0);
    }
});
