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

server.listen(9877, async () => {
    const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
    const edge = spawn(edgePath, [
        '--remote-debugging-port=9223',
        '--headless=new',
        '--no-sandbox',
        '--disable-gpu',
        '--user-data-dir=' + path.resolve('temp_edge_profile_paste'),
        'http://localhost:9877/index.html'
    ]);

    await new Promise(r => setTimeout(r, 2000));

    try {
        const res = await fetch('http://localhost:9223/json');
        const targets = await res.json();
        const pageTarget = targets.find(t => t.url.includes('9877'));
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
            await new Promise(r => setTimeout(r, 1500));

            console.log('--- TEST 1: Insert text between `` ---');
            await send('Runtime.evaluate', {
                expression: 'window.setMarkdown("hello world\\n\\n``")',
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 300));

            const test1 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const view = window.getEditorView();
                    let foundPos = -1;
                    view.state.doc.descendants((node, pos) => {
                        if (node.isText && node.text === "''".replace(/'/g, "\`")) {
                            foundPos = pos + 1;
                        }
                    });

                    // Insert "hello world" text between the backticks
                    const tr = view.state.tr.insertText('hello world', foundPos);
                    view.dispatch(tr);

                    return {
                        doc: view.state.doc.toJSON(),
                        markdown: window.getMarkdown()
                    };
                })()`,
                returnByValue: true
            });

            console.log('Test 1 doc:', JSON.stringify(test1.result.result.value.doc));
            console.log('Test 1 markdown:', JSON.stringify(test1.result.result.value.markdown));
            const hasInlineCode1 = JSON.stringify(test1.result.result.value.doc).includes('"inlineCode"');
            console.log('Test 1 has inlineCode mark:', hasInlineCode1);

            console.log('--- TEST 2: Typing `foo` via text insertion ---');
            await send('Runtime.evaluate', {
                expression: 'window.setMarkdown("Some text: ")',
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 300));

            const test2 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const view = window.getEditorView();
                    const endPos = view.state.doc.content.size - 1;
                    const tr = view.state.tr.insertText("'foo'".replace(/'/g, "\`"), endPos);
                    view.dispatch(tr);

                    return {
                        doc: view.state.doc.toJSON(),
                        markdown: window.getMarkdown()
                    };
                })()`,
                returnByValue: true
            });

            console.log('Test 2 doc:', JSON.stringify(test2.result.result.value.doc));
            console.log('Test 2 markdown:', JSON.stringify(test2.result.result.value.markdown));
            const hasInlineCode2 = JSON.stringify(test2.result.result.value.doc).includes('"inlineCode"');
            console.log('Test 2 has inlineCode mark:', hasInlineCode2);

            console.log('--- TEST 3: Select text and press backtick ---');
            await send('Runtime.evaluate', {
                expression: 'window.setMarkdown("Select this word here")',
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 300));

            const test3 = await send('Runtime.evaluate', {
                expression: `(() => {
                    try {
                        const view = window.getEditorView();
                        const { TextSelection } = view.state.selection.constructor;
                        // Selection from 8 to 17 ("this word")
                        const tr = view.state.tr.setSelection(view.state.selection.constructor.near(view.state.doc.resolve(8)).constructor.between(view.state.doc.resolve(8), view.state.doc.resolve(17)));
                        view.dispatch(tr);

                        const keyEvt = new KeyboardEvent('keydown', {
                            key: "'".replace(/'/g, "\`"),
                            bubbles: true,
                            cancelable: true
                        });
                        window.dispatchEvent(keyEvt);

                        return {
                            doc: view.state.doc.toJSON(),
                            markdown: window.getMarkdown()
                        };
                    } catch (e) {
                        return { error: e.message, stack: e.stack };
                    }
                })()`,
                returnByValue: true
            });

            console.log('Test 3 result:', JSON.stringify(test3.result.result.value));
            const hasInlineCode3 = JSON.stringify(test3.result.result.value.doc || '').includes('"inlineCode"');
            console.log('Test 3 has inlineCode mark:', hasInlineCode3);

            if (hasInlineCode1 && hasInlineCode2 && hasInlineCode3) {
                console.log('ALL INLINE CODE TESTS PASSED PERFECTLY!');
            } else {
                console.error('SOME TESTS FAILED!');
            }

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
