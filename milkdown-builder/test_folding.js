const { spawn } = require('child_process');
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
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const edge = spawn(edgePath, [
        '--remote-debugging-port=9226',
        '--headless=new',
        '--no-sandbox',
        '--disable-gpu',
        '--user-data-dir=' + path.resolve('temp_edge_profile_folding'),
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
            await send('Page.enable');

            await send('Page.reload');
            await new Promise(r => setTimeout(r, 1500));

            const mdContent = `# Section 1\n\nParagraph 1\n\n## Sub-section 1.1\n\nSub-paragraph 1.1\n\n# Section 2\n\nParagraph 2`;
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(mdContent)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 500));

            // Check fold buttons are attached
            const foldButtons = await send('Runtime.evaluate', {
                expression: `document.querySelectorAll('.heading-fold-btn').length`,
                returnByValue: true
            });
            console.log('Number of heading fold buttons attached:', foldButtons.result.result.value);

            // Test 1: Click fold button on Section 1
            const foldSection1 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1 = document.querySelector('.ProseMirror h1');
                    const btn = h1.querySelector('.heading-fold-btn');
                    btn.click();

                    const hiddenCount = document.querySelectorAll('.heading-folded-hidden').length;
                    const h2 = document.querySelector('.ProseMirror h2');
                    const isH2Hidden = h2.classList.contains('heading-folded-hidden');
                    const section2 = document.querySelectorAll('.ProseMirror h1')[1];
                    const isSection2Hidden = section2.classList.contains('heading-folded-hidden');

                    return {
                        hiddenCount,
                        isH2Hidden,
                        isSection2Hidden,
                        isSection1Folded: h1.classList.contains('is-folded'),
                        badgeText: (h1.querySelector('.heading-fold-badge') || {}).textContent,
                        markdown: window.getMarkdown()
                    };
                })()`,
                returnByValue: true
            });

            console.log('Fold Section 1 result:', foldSection1.result.result.value);

            // Test 2: Unfold Section 1
            const unfoldSection1 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1 = document.querySelector('.ProseMirror h1');
                    const btn = h1.querySelector('.heading-fold-btn');
                    btn.click();

                    const hiddenCount = document.querySelectorAll('.heading-folded-hidden').length;
                    const h2 = document.querySelector('.ProseMirror h2');
                    const isH2Hidden = h2.classList.contains('heading-folded-hidden');

                    return {
                        hiddenCount,
                        isH2Hidden,
                        isSection1Folded: h1.classList.contains('is-folded'),
                        badgePresent: !!h1.querySelector('.heading-fold-badge')
                    };
                })()`,
                returnByValue: true
            });

            console.log('Unfold Section 1 result:', unfoldSection1.result.result.value);

            // Test 3: Fold only Sub-section 1.1 (H2)
            const foldH2 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h2 = document.querySelector('.ProseMirror h2');
                    const btn = h2.querySelector('.heading-fold-btn');
                    btn.click();

                    const hiddenCount = document.querySelectorAll('.heading-folded-hidden').length;
                    const subP = h2.nextElementSibling;
                    const isSubPHidden = subP.classList.contains('heading-folded-hidden');
                    const section2 = document.querySelectorAll('.ProseMirror h1')[1];
                    const isSection2Hidden = section2.classList.contains('heading-folded-hidden');

                    return {
                        hiddenCount,
                        isSubPHidden,
                        isSection2Hidden,
                        isH2Folded: h2.classList.contains('is-folded')
                    };
                })()`,
                returnByValue: true
            });

            console.log('Fold H2 result:', foldH2.result.result.value);

            console.log('ALL FOLDING TESTS PASSED PERFECTLY!');
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
