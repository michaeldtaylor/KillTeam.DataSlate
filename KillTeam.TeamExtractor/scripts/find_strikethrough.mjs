/**
 * find_strikethrough.mjs
 *
 * Detects visually struck-through text in a PDF supplementary-info file using pdf.js.
 * PDF strikethrough is rendered as a thin horizontal vector path drawn over text — it is
 * not a text attribute and is invisible to text-extraction tools like pdftotext.
 *
 * Detection strategy:
 *   1. Walk the PDF operator stream looking for thin horizontal lines (lineWidth ≤ 0.5pt,
 *      width ≥ 15pt) drawn via the constructPath operator after a translation transform.
 *   2. For each struck line, find text items whose midline overlaps it.
 *   3. Group struck items that share the same horizontal line (y within ±2pt tolerance)
 *      and sort them by x-position.
 *   4. Join each group into a single phrase string.
 *
 * Output: JSON array of phrase strings, one per horizontal struck line.
 *
 * Usage: node find_strikethrough.mjs <path-to-pdf>
 */

import { getDocument, OPS } from 'pdfjs-dist/legacy/build/pdf.mjs';
import { readFileSync } from 'fs';

const pdfPath = process.argv[2];
if (!pdfPath) {
    process.stderr.write('Usage: node find_strikethrough.mjs <pdf-path>\n');
    process.exit(1);
}

const data = new Uint8Array(readFileSync(pdfPath));
const doc = await getDocument({ data }).promise;

/** @type {{ lineY: number, x: number, text: string }[]} */
const allStruckItems = [];

for (let pageNum = 1; pageNum <= doc.numPages; pageNum++) {
    const page = await doc.getPage(pageNum);
    const opList = await page.getOperatorList();
    const { fnArray, argsArray } = opList;

    // Find thin horizontal strikethrough lines.
    // Pattern: setLineWidth (thin) → save → transform [1,0,0,1,tx,ty] → constructPath [0,0,0,1,w,0] → restore
    const hlines = [];
    let currentLineWidth = 1;
    let pendingTransform = null;

    for (let i = 0; i < fnArray.length; i++) {
        const op = fnArray[i];
        const args = argsArray[i];

        if (op === OPS.setLineWidth) {
            currentLineWidth = args[0];
        }

        if (op === OPS.transform) {
            // Only care about pure translation transforms [1,0,0,1,tx,ty]
            if (args[0] === 1 && args[1] === 0 && args[2] === 0 && args[3] === 1) {
                pendingTransform = { tx: args[4], ty: args[5] };
            }
        }

        if (op === OPS.constructPath && pendingTransform && currentLineWidth <= 0.5) {
            // args[1] may be [Float32Array] or Float32Array directly — unwrap
            const rawPts = argsArray[i][1];
            const ptsTyped = Array.isArray(rawPts) ? rawPts[0] : rawPts;
            const pts = ptsTyped ? Array.from(ptsTyped) : [];
            // Simple horizontal line encoded as: [0 (moveTo), 0, 0, 1 (lineTo), w, 0]
            // Require width > 15pt to avoid false positives on tiny decorative marks
            if (pts.length === 6 && pts[0] === 0 && pts[1] === 0 && pts[2] === 0 &&
                pts[3] === 1 && Math.abs(pts[5]) < 0.01 && pts[4] > 15) {
                hlines.push({
                    x1: pendingTransform.tx,
                    x2: pendingTransform.tx + pts[4],
                    y: pendingTransform.ty,
                    lineWidth: currentLineWidth
                });
            }
            pendingTransform = null;
        }

        if (op === OPS.save || op === OPS.restore) {
            pendingTransform = null;
        }
    }

    if (hlines.length === 0) continue;

    // Get text content and find items whose midline is covered by a strikethrough line
    const tc = await page.getTextContent();

    for (const item of tc.items) {
        if (!item.str?.trim()) continue;
        const tx = item.transform;
        const ix = tx[4];   // text x start
        const iy = tx[5];   // text baseline y
        const ih = item.height;
        const iw = item.width;

        for (const line of hlines) {
            const midY = iy + ih * 0.5;
            if (Math.abs(line.y - midY) < ih * 0.6 &&
                line.x1 <= ix + iw * 0.9 &&
                line.x2 >= ix + iw * 0.1) {
                allStruckItems.push({ lineY: line.y, x: ix, text: item.str });
                break;
            }
        }
    }
}

// Group items by horizontal line (lineY within ±2pt tolerance), then sort by x within each group
/** @type {{ y: number, items: { x: number, text: string }[] }[]} */
const groups = [];
for (const item of allStruckItems) {
    const group = groups.find(g => Math.abs(g.y - item.lineY) < 2);
    if (group) {
        group.items.push({ x: item.x, text: item.text });
    } else {
        groups.push({ y: item.lineY, items: [{ x: item.x, text: item.text }] });
    }
}

// Sort items within each group by x position, join into phrase strings
const phrases = groups
    .map(g => {
        g.items.sort((a, b) => a.x - b.x);
        return g.items.map(i => i.text.trim()).filter(Boolean).join(' ');
    })
    .filter(Boolean);

// Output as JSON array of phrase strings for the C# tool to consume
process.stdout.write(JSON.stringify(phrases) + '\n');
