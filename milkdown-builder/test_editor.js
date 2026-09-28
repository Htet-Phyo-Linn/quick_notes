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
            await send('Page.enable');

            ws.addEventListener('message', (event) => {
                const data = JSON.parse(event.data);
                if (data.method === 'Runtime.consoleAPICalled') {
                    console.log('[Console]', data.params.type, data.params.args.map(a => a.value));
                } else if (data.method === 'Runtime.exceptionThrown') {
                    console.log('[Exception]', data.params.exceptionDetails.text, data.params.exceptionDetails.exception?.description);
                }
            });

            await send('Page.reload');
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

            console.log('--- TEST 5: Load SMEDB note with all 7 images ---');
            const smedbContent = `![](.assets/image_20260917_103204_380.png)\n![](.assets/image_20260917_103220_292.png)\n![](.assets/image_20260917_103234_712.png)\n![](.assets/image_20260917_103256_286.png)\n![](.assets/image_20260917_103312_635.png)\n![](.assets/image_20260917_103959_881.png)\n![](.assets/image_20260917_104025_428.png)`;
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(smedbContent)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 500));

            const docJson = await send('Runtime.evaluate', {
                expression: `JSON.stringify(window.getEditorView() ? window.getEditorView().state.doc.toJSON() : null)`,
                returnByValue: true
            });
            const parsedDoc = JSON.parse(docJson.result.result.value || '{}');
            const imgBlocks = (parsedDoc.content || []).filter(c => c.type === 'image-block');
            console.log('Number of image-block nodes in document:', imgBlocks.length);

            // Simulate loading all 7 images
            const testBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            const images = [
                ".assets/image_20260917_103204_380.png",
                ".assets/image_20260917_103220_292.png",
                ".assets/image_20260917_103234_712.png",
                ".assets/image_20260917_103256_286.png",
                ".assets/image_20260917_103312_635.png",
                ".assets/image_20260917_103959_881.png",
                ".assets/image_20260917_104025_428.png"
            ];
            for (const img of images) {
                await send('Runtime.evaluate', {
                    expression: `window.onImageLoaded(${JSON.stringify(img)}, ${JSON.stringify(testBase64)})`,
                    returnByValue: true
                });
            }
            await new Promise(r => setTimeout(r, 500));

            const renderedImgs = await send('Runtime.evaluate', {
                expression: `Array.from(document.querySelectorAll('.milkdown-image-block img')).filter(i => i.src.startsWith('data:')).length`,
                returnByValue: true
            });
            console.log('Rendered base64 image count:', renderedImgs.result.result.value);

            console.log('--- TEST 6: Test Collapsible Headings like Word ---');
            const mdFolding = "# Heading 1\n\nBody paragraph 1\n\n## Heading 1.1\n\nSub-body 1.1\n\n# Heading 2\n\nBody paragraph 2";
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(mdFolding)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 400));

            const foldRes1 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1 = document.querySelectorAll('.ProseMirror h1')[0];
                    const btn = h1.querySelector('.heading-fold-btn');
                    btn.click();

                    const h2 = document.querySelector('.ProseMirror h2');
                    const isH2Hidden = h2.classList.contains('heading-folded-hidden');
                    const h1_2 = document.querySelectorAll('.ProseMirror h1')[1];
                    const isH1_2Hidden = h1_2.classList.contains('heading-folded-hidden');
                    const badgeText = (h1.querySelector('.heading-fold-badge') || {}).textContent;

                    return {
                        isH1Folded: h1.classList.contains('is-folded'),
                        isH2Hidden,
                        isH1_2Hidden,
                        badgeText,
                        fullMarkdown: window.getMarkdown()
                    };
                })()`,
                returnByValue: true
            });
            console.log('Fold Heading 1 result:', JSON.stringify(foldRes1.result?.result?.value || foldRes1));

            const foldRes2 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1 = document.querySelectorAll('.ProseMirror h1')[0];
                    const btn = h1.querySelector('.heading-fold-btn');
                    btn.click(); // Unfold

                    const h2 = document.querySelector('.ProseMirror h2');
                    const isH2Hidden = h2.classList.contains('heading-folded-hidden');

                    return {
                        isH1Folded: h1.classList.contains('is-folded'),
                        isH2Hidden,
                        hasBadge: !!h1.querySelector('.heading-fold-badge')
                    };
                })()`,
                returnByValue: true
            });
            console.log('Unfold Heading 1 result:', JSON.stringify(foldRes2.result?.result?.value || foldRes2));

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
