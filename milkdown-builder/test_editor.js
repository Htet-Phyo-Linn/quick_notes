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

            console.log('--- TEST: Fold Untitled.md with hello world and pasted image ---');
            const untitledMd = fs.readFileSync('C:\\Users\\PC\\OneDrive - BIM ADVANCED TECHNOLOGY SERVICES PTE. LTD\\QuickNote\\Untitled.md', 'utf8');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(untitledMd)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 600));

            const foldUntitled = await send('Runtime.evaluate', {
                awaitPromise: true,
                expression: `(async () => {
                    const h2 = document.querySelector('.ProseMirror h2');
                    if (!h2) return { error: 'No h2 found' };
                    const btn = h2.querySelector('.heading-fold-btn');
                    const beforeH2Rect = h2.getBoundingClientRect();
                    const beforeBtnRect = btn ? btn.getBoundingClientRect() : null;

                    btn.click(); // Fold h2
                    await new Promise(r => setTimeout(r, 100));

                    const currentBtn = h2.querySelector('.heading-fold-btn');
                    const btnRect = currentBtn ? currentBtn.getBoundingClientRect() : { top: -1, height: -1, bottom: -1 };
                    const h2Rect = h2.getBoundingClientRect();
                    const badge = h2.querySelector('.heading-fold-badge');
                    const badgeRect = badge ? badge.getBoundingClientRect() : null;
                    const imgBlock = document.querySelector('.milkdown-image-block');

                    return {
                        beforeH2Height: beforeH2Rect.height,
                        foldedH2Height: h2Rect.height,
                        isH2Folded: h2.classList.contains('is-folded'),
                        btnHeight: btnRect.height,
                        imgBlockDisplay: imgBlock ? window.getComputedStyle(imgBlock).display : null,
                        hasImgFoldedClass: imgBlock ? imgBlock.classList.contains('heading-folded-hidden') : false,
                        btnCenterDiff: (btnRect.top + btnRect.height / 2) - (h2Rect.top + h2Rect.height / 2)
                    };
                })()`,
                returnByValue: true
            });
            console.log('Untitled.md Fold Result (H2 + Image):', JSON.stringify(foldUntitled.result?.result?.value || foldUntitled));

            console.log('--- TEST: Fold Linux Heading and check alignment ---');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown('# Linux\\n\\nSome Linux content\\n\\n![img](test.png)')`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 300));

            const foldLinux = await send('Runtime.evaluate', {
                awaitPromise: true,
                expression: `(async () => {
                    const h1 = document.querySelector('.ProseMirror h1');
                    const btn = h1.querySelector('.heading-fold-btn');
                    const beforeBtnRect = btn.getBoundingClientRect();
                    const beforeH1Rect = h1.getBoundingClientRect();

                    btn.click(); // Fold h1
                    await new Promise(r => setTimeout(r, 100));

                    const currentBtn = h1.querySelector('.heading-fold-btn');
                    const btnRect = currentBtn.getBoundingClientRect();
                    const h1Rect = h1.getBoundingClientRect();
                    const badge = h1.querySelector('.heading-fold-badge');
                    const badgeRect = badge ? badge.getBoundingClientRect() : null;
                    const imgBlock = document.querySelector('.milkdown-image-block');

                    return {
                        isH1Folded: h1.classList.contains('is-folded'),
                        h1Height: h1Rect.height,
                        btnCenterDiff: (btnRect.top + btnRect.height / 2) - (h1Rect.top + h1Rect.height / 2),
                        imgBlockDisplay: imgBlock ? window.getComputedStyle(imgBlock).display : null,
                        hasBadge: !!badge
                    };
                })()`,
                returnByValue: true
            });
            console.log('Linux Heading Fold Result:', JSON.stringify(foldLinux.result?.result?.value || foldLinux));

            const foldRes2 = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1 = document.querySelectorAll('.ProseMirror h1')[0];
                    const btn = h1.querySelector('.heading-fold-btn');
                    btn.click(); // Unfold
                    return {
                        isH1Folded: h1.classList.contains('is-folded'),
                        hasBadge: !!h1.querySelector('.heading-fold-badge')
                    };
                })()`,
                returnByValue: true
            });
            console.log('Unfold Heading 1 result:', JSON.stringify(foldRes2.result?.result?.value || foldRes2));

            console.log('--- TEST 7: Enter at offset 0 of Heading inserts line above ---');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown("# My Title\\n\\nSome body text")`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 400));

            const enterTest = await send('Runtime.evaluate', {
                expression: `(() => {
                    const view = window.getEditorView();
                    // Place cursor at pos 1 (offset 0 in heading)
                    const sel = view.state.selection.constructor.near(view.state.doc.resolve(1));
                    view.dispatch(view.state.tr.setSelection(sel));

                    // Dispatch Enter keydown
                    const enterEvt = new KeyboardEvent('keydown', {
                        key: 'Enter',
                        bubbles: true,
                        cancelable: true
                    });
                    window.dispatchEvent(enterEvt);

                    const doc = view.state.doc.toJSON();
                    const firstBlock = doc.content[0];
                    const secondBlock = doc.content[1];

                    return {
                        firstBlockType: firstBlock ? firstBlock.type : null,
                        secondBlockType: secondBlock ? secondBlock.type : null,
                        secondBlockText: (secondBlock && secondBlock.content) ? secondBlock.content[0].text : null,
                        markdown: window.getMarkdown()
                    };
                })()`,
                returnByValue: true
            });

            console.log('Enter at heading start result:', JSON.stringify(enterTest.result?.result?.value || enterTest));

            console.log('--- TEST 8: Test Folding with Images, Tables, Lists ---');
            const complexFolding = `# Section 1\n\n![test image](image.png)\n\n| Col1 | Col2 |\n| --- | --- |\n| Val1 | Val2 |\n\n- List Item 1\n- List Item 2\n\n# Section 2\n\nOutside paragraph`;
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown(${JSON.stringify(complexFolding)})`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 600));

            const foldComplex = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1_1 = document.querySelectorAll('.ProseMirror h1')[0];
                    const btn = h1_1.querySelector('.heading-fold-btn');
                    btn.click(); // Fold Section 1

                    const imgBlock = document.querySelector('.milkdown-image-block');
                    const table = document.querySelector('table');
                    const list = document.querySelector('ul');
                    const h1_2 = document.querySelectorAll('.ProseMirror h1')[1];

                    return {
                        isSection1Folded: h1_1.classList.contains('is-folded'),
                        isImgHidden: imgBlock ? (imgBlock.classList.contains('heading-folded-hidden') || window.getComputedStyle(imgBlock).display === 'none') : 'no-img',
                        isTableHidden: table ? (table.classList.contains('heading-folded-hidden') || table.closest('.heading-folded-hidden') !== null || window.getComputedStyle(table).display === 'none') : 'no-table',
                        isListHidden: list ? (list.classList.contains('heading-folded-hidden') || window.getComputedStyle(list).display === 'none') : 'no-list',
                        isSection2Visible: !h1_2.classList.contains('heading-folded-hidden')
                    };
                })()`,
                returnByValue: true
            });
            console.log('Fold Section 1 (with image, table, list) result:', JSON.stringify(foldComplex.result?.result?.value || foldComplex));

            const unfoldComplex = await send('Runtime.evaluate', {
                expression: `(() => {
                    const h1_1 = document.querySelectorAll('.ProseMirror h1')[0];
                    const btn = h1_1.querySelector('.heading-fold-btn');
                    btn.click(); // Unfold Section 1

                    const imgBlock = document.querySelector('.milkdown-image-block');
                    const table = document.querySelector('table');
                    const list = document.querySelector('ul');

                    return {
                        isSection1Folded: h1_1.classList.contains('is-folded'),
                        isImgVisible: imgBlock ? !imgBlock.classList.contains('heading-folded-hidden') : false,
                        isTableVisible: table ? (!table.classList.contains('heading-folded-hidden') && table.closest('.heading-folded-hidden') === null) : false,
                        isListVisible: list ? !list.classList.contains('heading-folded-hidden') : false
                    };
                })()`,
                returnByValue: true
            });
            console.log('Unfold Section 1 result:', JSON.stringify(unfoldComplex.result?.result?.value || unfoldComplex));

            console.log('--- TEST 9: Multi-Select with Cursor & Right-Click Context Menu ---');
            await send('Runtime.evaluate', {
                expression: `window.setMarkdown("Line 1 first paragraph\\n\\nLine 2 second paragraph\\n\\nLine 3 third paragraph")`,
                returnByValue: true
            });
            await new Promise(r => setTimeout(r, 400));

            // Select across multiple paragraphs
            const dragCoords = await send('Runtime.evaluate', {
                expression: `(() => {
                    const ps = document.querySelectorAll(".ProseMirror p");
                    const r1 = document.createRange();
                    r1.selectNodeContents(ps[0].firstChild);
                    const rect1 = r1.getBoundingClientRect();

                    const r2 = document.createRange();
                    r2.selectNodeContents(ps[1].firstChild);
                    const rect2 = r2.getBoundingClientRect();

                    return {
                        x1: rect1.left + 5, y1: rect1.top + rect1.height / 2,
                        x2: rect2.right - 5, y2: rect2.top + rect2.height / 2
                    };
                })()`,
                returnByValue: true
            });
            const dc = dragCoords.result.result.value;

            // Perform mouse drag
            await send('Input.dispatchMouseEvent', {
                type: 'mousePressed',
                x: dc.x1,
                y: dc.y1,
                button: 'left',
                buttons: 1,
                clickCount: 1
            });
            for (let i = 1; i <= 10; i++) {
                const curX = Math.round(dc.x1 + (dc.x2 - dc.x1) * (i / 10));
                const curY = Math.round(dc.y1 + (dc.y2 - dc.y1) * (i / 10));
                await send('Input.dispatchMouseEvent', {
                    type: 'mouseMoved',
                    x: curX,
                    y: curY,
                    button: 'left',
                    buttons: 1
                });
                await new Promise(r => setTimeout(r, 10));
            }
            await send('Input.dispatchMouseEvent', {
                type: 'mouseReleased',
                x: dc.x2,
                y: dc.y2,
                button: 'left',
                buttons: 0
            });
            await new Promise(r => setTimeout(r, 200));

            const dragResult = await send('Runtime.evaluate', {
                expression: `(() => {
                    const view = window.getEditorView();
                    const domSel = window.getSelection().toString();
                    const pmSel = view ? { from: view.state.selection.from, to: view.state.selection.to, empty: view.state.selection.empty } : null;
                    return { hasSelection: !pmSel.empty && domSel.length > 0, domSel, pmSel };
                })()`,
                returnByValue: true
            });
            console.log('Drag Multi-Selection Result:', JSON.stringify(dragResult.result?.result?.value || dragResult));

            // Right click on the selection
            await send('Input.dispatchMouseEvent', {
                type: 'mousePressed',
                x: dc.x1 + 15,
                y: dc.y1,
                button: 'right',
                clickCount: 1
            });
            await send('Input.dispatchMouseEvent', {
                type: 'mouseReleased',
                x: dc.x1 + 15,
                y: dc.y1,
                button: 'right'
            });
            await new Promise(r => setTimeout(r, 200));

            const rightClickOnSelection = await send('Runtime.evaluate', {
                expression: `(() => {
                    const menu = document.getElementById("format-menu");
                    const cutBtn = document.getElementById("menu-cut");
                    const copyBtn = document.getElementById("menu-copy");
                    const pasteBtn = document.getElementById("menu-paste");
                    const selectAllBtn = document.getElementById("menu-select-all");
                    const domSel = window.getSelection().toString();
                    const view = window.getEditorView();
                    const pmSel = view ? { from: view.state.selection.from, to: view.state.selection.to, empty: view.state.selection.empty } : null;

                    return {
                        menuVisible: menu.style.display === 'block',
                        cutEnabled: cutBtn && !cutBtn.disabled,
                        copyEnabled: copyBtn && !copyBtn.disabled,
                        hasPasteBtn: !!pasteBtn,
                        hasSelectAllBtn: !!selectAllBtn,
                        selectionPreserved: !pmSel.empty && domSel.length > 0
                    };
                })()`,
                returnByValue: true
            });
            console.log('Right-Click on Selection Result:', JSON.stringify(rightClickOnSelection.result?.result?.value || rightClickOnSelection));

            // Test Select All
            const selectAllResult = await send('Runtime.evaluate', {
                expression: `(() => {
                    const selectAllBtn = document.getElementById("menu-select-all");
                    selectAllBtn.click();
                    const view = window.getEditorView();
                    return {
                        allSelected: view && view.state.selection.from === 0 && view.state.selection.to === view.state.doc.content.size
                    };
                })()`,
                returnByValue: true
            });
            console.log('Select All Result:', JSON.stringify(selectAllResult.result?.result?.value || selectAllResult));

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
