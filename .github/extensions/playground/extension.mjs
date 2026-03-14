// Extension: playground
// Interactive browser-based document review with per-section annotations and feedback submission
//
// NOTE: As of Copilot CLI v1.0.4, the extension launcher has a bug where it passes the extension
// path directly to the copilot binary (invalid) instead of via the bootstrap preload.
// This extension is ready to enable once that bug is fixed. Until then, the playground skill
// uses a PowerShell-based fallback that generates HTML + opens in browser (see SKILL.md).
// Track: "Invalid command format. Did you mean: copilot -i <bootstrap>?" error on extension launch.

import { approveAll } from "@github/copilot-sdk";
import { joinSession } from "@github/copilot-sdk/extension";
import { createServer } from "node:http";
import { exec } from "node:child_process";

function escHtml(s) {
    return String(s)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

function generateHtml(markdown, title) {
    const safeTitle = escHtml(title);
    const jsonMarkdown = JSON.stringify(markdown);
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>${safeTitle}</title>
  <script src="https://cdn.jsdelivr.net/npm/marked@12/marked.min.js"><\/script>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body { background: #f6f8fa; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; color: #24292f; line-height: 1.6; }
    .layout { display: grid; grid-template-columns: 1fr 340px; gap: 20px; max-width: 1200px; margin: 0 auto; padding: 24px 16px; align-items: start; }
    @media (max-width: 820px) { .layout { grid-template-columns: 1fr; } }

    .content { background: #fff; border: 1px solid #d0d7de; border-radius: 6px; padding: 32px 40px; }
    .content h1 { font-size: 2em; border-bottom: 1px solid #d0d7de; padding-bottom: .4em; margin-bottom: 1em; }
    .content h2 { font-size: 1.5em; border-bottom: 1px solid #eaecef; padding-bottom: .3em; margin: 1.5em 0 .75em; }
    .content h3 { font-size: 1.25em; margin: 1.25em 0 .5em; }
    .content h4 { font-size: 1em; margin: 1em 0 .4em; }
    .content p { margin-bottom: .8em; }
    .content ul, .content ol { padding-left: 2em; margin-bottom: .8em; }
    .content li { margin-bottom: .2em; }
    .content code { background: #eef0f3; border-radius: 4px; padding: 2px 6px; font-family: "SFMono-Regular", Consolas, monospace; font-size: .9em; }
    .content pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 6px; padding: 16px; overflow-x: auto; margin-bottom: 1em; }
    .content pre code { background: none; padding: 0; font-size: .88em; }
    .content blockquote { border-left: 4px solid #d0d7de; padding-left: 1em; color: #57606a; margin-bottom: .8em; }
    .content table { border-collapse: collapse; width: 100%; margin-bottom: 1em; font-size: .9em; }
    .content th, .content td { border: 1px solid #d0d7de; padding: 6px 12px; }
    .content th { background: #f6f8fa; font-weight: 600; }
    .content hr { border: none; border-top: 1px solid #d0d7de; margin: 1.5em 0; }
    .content a { color: #0969da; }
    .content .annotated-heading { background: #fffbdd; border-radius: 3px; }

    .sidebar { position: sticky; top: 24px; }
    .card { background: #fff; border: 1px solid #d0d7de; border-radius: 6px; padding: 16px; }
    .card + .card { margin-top: 16px; }
    .card-title { font-size: 13px; font-weight: 600; color: #24292f; margin-bottom: 10px; }

    .section-list { list-style: none; font-size: 13px; max-height: 320px; overflow-y: auto; }
    .section-list li { padding: 3px 0; border-bottom: 1px solid #f3f4f5; }
    .section-list li:last-child { border-bottom: none; }
    .sec-row { display: flex; align-items: baseline; gap: 6px; }
    .sec-link { color: #0969da; text-decoration: none; flex: 1; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
    .sec-link:hover { text-decoration: underline; }
    .sec-link.h3 { padding-left: 12px; color: #57606a; font-size: 12px; }
    .note-btn { border: none; background: none; color: #0969da; cursor: pointer; font-size: 12px; padding: 1px 4px; border-radius: 3px; white-space: nowrap; flex-shrink: 0; }
    .note-btn:hover { background: #ddf4ff; }
    .note-btn.has-note { color: #1a7f37; }
    .sec-note { display: none; margin: 4px 0 8px 0; }
    .sec-note.open { display: block; }
    .sec-note textarea { width: 100%; min-height: 60px; border: 1px solid #d0d7de; border-radius: 4px; padding: 7px; font-size: 12px; font-family: inherit; resize: vertical; }

    .global-notes { width: 100%; min-height: 100px; border: 1px solid #d0d7de; border-radius: 4px; padding: 8px; font-size: 13px; font-family: inherit; resize: vertical; }
    .btn-row { margin-top: 10px; display: flex; flex-direction: column; gap: 6px; }
    .btn-submit { background: #1a7f37; color: #fff; border: none; border-radius: 6px; padding: 9px; font-size: 14px; font-weight: 600; cursor: pointer; }
    .btn-submit:hover:not(:disabled) { background: #116329; }
    .btn-submit:disabled { background: #8c959f; cursor: default; }
    .btn-skip { background: none; border: 1px solid #d0d7de; border-radius: 6px; padding: 7px; font-size: 13px; color: #57606a; cursor: pointer; }
    .btn-skip:hover { background: #f6f8fa; }
    .banner { display: none; margin-top: 10px; padding: 10px; background: #dafbe1; border: 1px solid #1a7f37; border-radius: 6px; color: #116329; font-size: 13px; }
  </style>
</head>
<body>
  <div class="layout">
    <div class="content" id="content"></div>
    <aside class="sidebar">
      <div class="card">
        <div class="card-title">📑 Sections</div>
        <ul class="section-list" id="secList"></ul>
      </div>
      <div class="card">
        <div class="card-title">📝 Overall Notes</div>
        <textarea class="global-notes" id="globalNotes" placeholder="General feedback, questions, anything you want to add…"></textarea>
        <div class="btn-row">
          <button class="btn-submit" id="submitBtn" onclick="submitFeedback()">Submit Feedback</button>
          <button class="btn-skip" onclick="skipFeedback()">No feedback — continue</button>
        </div>
        <div class="banner" id="banner"></div>
      </div>
    </aside>
  </div>

  <script>
    const raw = ${jsonMarkdown};
    document.getElementById("content").innerHTML = marked.parse(raw);

    const secList = document.getElementById("secList");
    const sectionNotes = {};
    const headings = Array.from(document.querySelectorAll("#content h2, #content h3"));

    headings.forEach((el, i) => {
      el.id = "sec-" + i;
      const label = el.textContent.trim();
      const isH3 = el.tagName === "H3";

      const li = document.createElement("li");

      const row = document.createElement("div");
      row.className = "sec-row";

      const a = document.createElement("a");
      a.href = "#sec-" + i;
      a.className = "sec-link" + (isH3 ? " h3" : "");
      a.textContent = label;

      const btn = document.createElement("button");
      btn.className = "note-btn";
      btn.id = "nbtn-" + i;
      btn.textContent = "+ note";
      btn.onclick = () => toggleNote(i);

      row.appendChild(a);
      row.appendChild(btn);

      const noteDiv = document.createElement("div");
      noteDiv.className = "sec-note";
      noteDiv.id = "snote-" + i;
      const ta = document.createElement("textarea");
      ta.id = "sta-" + i;
      ta.placeholder = 'Comment on "' + label.replace(/"/g, "'") + '"…';
      ta.addEventListener("input", () => updateNote(i, label, el));
      noteDiv.appendChild(ta);

      li.appendChild(row);
      li.appendChild(noteDiv);
      secList.appendChild(li);
    });

    function toggleNote(i) {
      const note = document.getElementById("snote-" + i);
      if (note.classList.toggle("open")) document.getElementById("sta-" + i).focus();
    }

    function updateNote(i, label, el) {
      const val = document.getElementById("sta-" + i).value.trim();
      const btn = document.getElementById("nbtn-" + i);
      if (val) {
        sectionNotes[i] = { section: label, comment: val };
        btn.classList.add("has-note");
        btn.textContent = "✓ note";
        el.classList.add("annotated-heading");
      } else {
        delete sectionNotes[i];
        btn.classList.remove("has-note");
        btn.textContent = "+ note";
        el.classList.remove("annotated-heading");
      }
    }

    function submitFeedback() {
      const global = document.getElementById("globalNotes").value.trim();
      send({ skipped: false, sectionComments: Object.values(sectionNotes), globalNotes: global });
    }

    function skipFeedback() {
      send({ skipped: true, sectionComments: [], globalNotes: "" });
    }

    function send(payload) {
      document.getElementById("submitBtn").disabled = true;
      fetch("/feedback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      })
        .then(() => {
          const banner = document.getElementById("banner");
          banner.textContent = payload.skipped
            ? "✅ Continuing without feedback. You can close this tab."
            : "✅ Feedback submitted! You can close this tab.";
          banner.style.display = "block";
        })
        .catch(() => {
          document.getElementById("submitBtn").disabled = false;
          alert("Submission failed — please try again.");
        });
    }
  <\/script>
</body>
</html>`;
}

const session = await joinSession({
    onPermissionRequest: approveAll,
    tools: [
        {
            name: "playground_serve",
            description: "Renders a markdown document as an interactive review page in the user's browser. Sections are listed in a sidebar — the user clicks '+ note' next to any heading to annotate it, adds overall notes, and submits. Returns all feedback as structured text. Use this for Human Review in the spec skill.",
            parameters: {
                type: "object",
                properties: {
                    markdown: {
                        type: "string",
                        description: "The full markdown content to render for review",
                    },
                    title: {
                        type: "string",
                        description: "Page title shown in the browser tab (default: 'Document Review')",
                    },
                    timeout_minutes: {
                        type: "number",
                        description: "Minutes to wait for the user to submit before timing out (default: 30)",
                    },
                },
                required: ["markdown"],
            },
            handler: async (args) => {
                const title = args.title || "Document Review";
                const timeoutMs = (args.timeout_minutes ?? 30) * 60 * 1000;
                const html = generateHtml(args.markdown, title);

                return new Promise((resolve, reject) => {
                    let settled = false;

                    const settle = (value) => {
                        if (settled) return;
                        settled = true;
                        clearTimeout(timer);
                        server.close();
                        resolve(value);
                    };

                    const server = createServer((req, res) => {
                        if (req.method === "GET" && req.url === "/") {
                            res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
                            res.end(html);
                            return;
                        }
                        if (req.method === "POST" && req.url === "/feedback") {
                            let body = "";
                            req.on("data", (chunk) => (body += chunk));
                            req.on("end", () => {
                                res.writeHead(200, { "Content-Type": "text/plain" });
                                res.end("OK");
                                try {
                                    const fb = JSON.parse(body);
                                    if (fb.skipped) {
                                        settle("User chose to continue without feedback.");
                                        return;
                                    }
                                    const lines = [];
                                    if (fb.sectionComments?.length) {
                                        lines.push("Section comments:");
                                        for (const c of fb.sectionComments) {
                                            lines.push(`  [${c.section}]: ${c.comment}`);
                                        }
                                    }
                                    if (fb.globalNotes) {
                                        lines.push(`Overall notes: ${fb.globalNotes}`);
                                    }
                                    settle(
                                        lines.length
                                            ? lines.join("\n")
                                            : "User submitted with no comments — no changes needed."
                                    );
                                } catch {
                                    settle(`Raw feedback: ${body}`);
                                }
                            });
                            return;
                        }
                        res.writeHead(404);
                        res.end();
                    });

                    const timer = setTimeout(() => {
                        settle("Playground timed out — no feedback received.");
                    }, timeoutMs);

                    server.on("error", (err) => {
                        if (!settled) {
                            settled = true;
                            clearTimeout(timer);
                            reject(new Error(`Playground server error: ${err.message}`));
                        }
                    });

                    server.listen(0, "127.0.0.1", () => {
                        const { port } = server.address();
                        const url = `http://localhost:${port}`;
                        if (process.platform === "win32") {
                            exec(`start "" "${url}"`, () => {});
                        } else if (process.platform === "darwin") {
                            exec(`open "${url}"`, () => {});
                        } else {
                            exec(`xdg-open "${url}"`, () => {});
                        }
                        session.log(`📋 Playground opened: ${url} — waiting for your feedback…`);
                    });
                });
            },
        },
    ],
});

