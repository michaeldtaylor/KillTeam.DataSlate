---
name: playground
description: Launch an interactive browser-based document review page. Use for Human Review in the spec skill, or any time you want the user to read and annotate a markdown document. Triggers on "human review", "playground", "document critique", "review this spec", "annotate".
---

# Playground Skill

Render a markdown document as a beautiful HTML page and open it in the user's browser. The page auto-detects **decision points** and renders Accept / Reject / Comment controls for each. As decisions are reviewed, a structured output panel builds up in real time. The user copies it and pastes it back into the CLI.

> **Note:** No extension required — generates a self-contained HTML file and opens it with PowerShell.

---

## How to Use

### Step 1 — Read the document

Use the `view` tool to read the target file (e.g. a spike file or `spec.md`).

### Step 2 — Generate the HTML file

Run this PowerShell block, substituting `PATH_TO_FILE` and `DOCUMENT_TITLE`:

```powershell
$markdown = Get-Content "PATH_TO_FILE" -Raw
$jsonMd   = $markdown | ConvertTo-Json -Compress
$docTitle = "DOCUMENT_TITLE"
```

Then build the HTML using the template below, save it to a temp file, and open it:

```powershell
$outPath = [System.IO.Path]::GetTempFileName() -replace '\.tmp$', '.html'
Set-Content -Path $outPath -Value $html -Encoding UTF8
Start-Process $outPath
Write-Host "Opened: $outPath"
```

### HTML Template

The `$html` here-string to use (paste verbatim, substituting `$jsonMd` and `$docTitle`):

```
$html = @"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>$docTitle</title>
  <script src="https://cdn.jsdelivr.net/npm/marked@12/marked.min.js"></script>
  <style>
    *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
    body{background:#f6f8fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;color:#24292f}
    .layout{display:grid;grid-template-columns:1fr 360px;gap:20px;max-width:1260px;margin:0 auto;padding:24px 16px;align-items:start}
    @media(max-width:860px){.layout{grid-template-columns:1fr}}
    .notice{background:#ddf4ff;border:1px solid #0969da;border-radius:6px;padding:12px 16px;margin-bottom:20px;font-size:13px;color:#0550ae}
    .content{background:#fff;border:1px solid #d0d7de;border-radius:6px;padding:32px 40px}
    .content h1{font-size:2em;border-bottom:1px solid #d0d7de;padding-bottom:.4em;margin-bottom:1em}
    .content h2{font-size:1.5em;border-bottom:1px solid #eaecef;padding-bottom:.3em;margin:1.5em 0 .75em}
    .content h3{font-size:1.25em;margin:1.25em 0 .5em}
    .content h4{font-size:1em;margin:1em 0 .4em}
    .content p{margin-bottom:.8em}
    .content ul,.content ol{padding-left:2em;margin-bottom:.8em}
    .content li{margin-bottom:.2em}
    .content code{background:#eef0f3;border-radius:4px;padding:2px 6px;font-family:'SFMono-Regular',Consolas,monospace;font-size:.9em}
    .content pre{background:#f6f8fa;border:1px solid #d0d7de;border-radius:6px;padding:16px;overflow-x:auto;margin-bottom:1em}
    .content pre code{background:none;padding:0;font-size:.88em}
    .content blockquote{border-left:4px solid #d0d7de;padding-left:1em;color:#57606a;margin-bottom:.8em}
    .content table{border-collapse:collapse;width:100%;margin-bottom:1em;font-size:.9em}
    .content th,.content td{border:1px solid #d0d7de;padding:6px 12px}
    .content th{background:#f6f8fa;font-weight:600}
    .content hr{border:none;border-top:1px solid #d0d7de;margin:1.5em 0}
    .content a{color:#0969da}
    .decision-widget{margin:6px 0 18px;padding:10px 14px;background:#f6f8fa;border:1px solid #d0d7de;border-radius:6px;display:flex;flex-wrap:wrap;gap:8px;align-items:flex-start}
    .dec-btn{border:none;border-radius:5px;padding:5px 14px;font-size:13px;font-weight:600;cursor:pointer}
    .dec-btn.accept{background:#dafbe1;color:#116329;border:1px solid #4ac26b}
    .dec-btn.reject{background:#ffebe9;color:#b91c1c;border:1px solid #f97583}
    .dec-btn.accept.active{background:#1a7f37;color:#fff}
    .dec-btn.reject.active{background:#cf222e;color:#fff}
    .dec-comment-wrap{flex:1 1 100%}
    .dec-label{font-size:11px;color:#57606a;font-style:italic;margin-bottom:2px}
    .dec-comment{width:100%;min-height:56px;border:1px solid #d0d7de;border-radius:4px;padding:7px 10px;font-size:12px;font-family:inherit;resize:vertical;margin-top:4px}
    .sidebar{position:sticky;top:24px;display:flex;flex-direction:column;gap:16px}
    .card{background:#fff;border:1px solid #d0d7de;border-radius:6px;padding:16px}
    .card-title{font-size:13px;font-weight:600;margin-bottom:10px;color:#24292f}
    .toc{list-style:none;font-size:12px;max-height:260px;overflow-y:auto}
    .toc li{padding:2px 0;border-bottom:1px solid #f3f4f5}
    .toc li:last-child{border-bottom:none}
    .toc a{color:#0969da;text-decoration:none}
    .toc a:hover{text-decoration:underline}
    .toc a.h3{padding-left:12px;color:#57606a}
    .toc a.decision{font-weight:600}
    .toc a.decision.done::after{content:" ✓";color:#1a7f37}
    .output-area{width:100%;min-height:180px;border:1px solid #d0d7de;border-radius:4px;padding:10px;font-size:12px;font-family:'SFMono-Regular',Consolas,monospace;background:#f6f8fa;resize:vertical;color:#24292f}
    .btn-row{display:flex;gap:8px;margin-top:10px}
    .btn-copy{flex:1;background:#0969da;color:#fff;border:none;border-radius:6px;padding:9px;font-size:13px;font-weight:600;cursor:pointer}
    .btn-copy:hover{background:#0860ca}
    .btn-copy.copied{background:#1a7f37}
    .progress{font-size:12px;color:#57606a;margin-bottom:8px}
  </style>
</head>
<body>
<div class="layout">
  <div>
    <div class="notice">📋 <strong>Review mode</strong> — Accept or reject each decision below, add optional comments, then <strong>Copy to Clipboard</strong> in the sidebar and paste back into Copilot CLI.</div>
    <div class="content" id="md"></div>
  </div>
  <aside class="sidebar">
    <div class="card">
      <div class="card-title">📑 Contents</div>
      <ul class="toc" id="toc"></ul>
    </div>
    <div class="card">
      <div class="card-title">📋 Review Output</div>
      <div class="progress" id="progress">0 of 0 decisions reviewed</div>
      <textarea class="output-area" id="outputArea" readonly placeholder="Decisions will appear here as you review…"></textarea>
      <div class="btn-row">
        <button class="btn-copy" id="copyBtn" onclick="copyOutput()">Copy to Clipboard</button>
      </div>
    </div>
  </aside>
</div>
<script>
  const raw = $jsonMd;
  document.getElementById('md').innerHTML = marked.parse(raw);
  const toc = document.getElementById('toc');
  const decisions = [];
  const DECISION_RE = /^(decision\s+\d|ac-\d{3}|us-\d{3})/i;
  document.querySelectorAll('#md h2,#md h3,#md h4').forEach((el,i) => {
    el.id = 'sec'+i;
    const label = el.textContent.trim();
    const isDec = DECISION_RE.test(label);
    const li = document.createElement('li');
    const a = document.createElement('a');
    a.href='#sec'+i; a.textContent=label;
    a.className=(el.tagName==='H3'?'h3':el.tagName==='H4'?'h4':'')+(isDec?' decision':'');
    a.id='toc'+i; li.appendChild(a); toc.appendChild(li);
    if(isDec){
      const idx=decisions.length;
      decisions.push({label,status:null,comment:'',tocLink:a});
      const w=document.createElement('div');
      w.className='decision-widget'; w.id='widget'+idx;
      w.innerHTML='<button class="dec-btn accept" onclick="setDec('+idx+',\'accept\')">✅ Accept</button>'
        +'<button class="dec-btn reject" onclick="setDec('+idx+',\'reject\')">❌ Reject</button>'
        +'<div class="dec-comment-wrap"><div class="dec-label">Optional comment:</div>'
        +'<textarea class="dec-comment" id="cmt'+idx+'" placeholder="Leave blank or add a note…" oninput="onCmt('+idx+')"></textarea></div>';
      // Insert after the Question: paragraph if present, otherwise after the heading
      let insertAfter = el;
      let sib = el.nextElementSibling;
      while (sib && !sib.matches('h2,h3,h4')) {
        const strong = sib.querySelector('strong');
        if (strong && strong.textContent.trim() === 'Question:') { insertAfter = sib; break; }
        sib = sib.nextElementSibling;
      }
      insertAfter.insertAdjacentElement('afterend',w);
    }
  });
  updateProgress();
  function setDec(idx,s){
    decisions[idx].status=decisions[idx].status===s?null:s;
    const w=document.getElementById('widget'+idx);
    w.querySelector('.accept').classList.toggle('active',decisions[idx].status==='accept');
    w.querySelector('.reject').classList.toggle('active',decisions[idx].status==='reject');
    decisions[idx].tocLink.classList.toggle('done',decisions[idx].status!==null);
    updateOutput(); updateProgress();
  }
  function onCmt(idx){decisions[idx].comment=document.getElementById('cmt'+idx).value.trim();updateOutput();}
  function updateProgress(){
    const done=decisions.filter(d=>d.status).length;
    document.getElementById('progress').textContent=done+' of '+decisions.length+' decisions reviewed';
  }
  function updateOutput(){
    const lines=['=== Review: $docTitle ===',''];
    decisions.forEach(d=>{
      const icon=d.status==='accept'?'ACCEPTED':d.status==='reject'?'REJECTED':'NOT REVIEWED';
      lines.push(d.label); lines.push('  '+icon);
      if(d.comment)lines.push('  Comment: '+d.comment);
      lines.push('');
    });
    document.getElementById('outputArea').value=lines.join('\n');
  }
  function copyOutput(){
    navigator.clipboard.writeText(document.getElementById('outputArea').value).then(()=>{
      const b=document.getElementById('copyBtn');
      b.textContent='✓ Copied!'; b.classList.add('copied');
      setTimeout(()=>{b.textContent='Copy to Clipboard';b.classList.remove('copied');},2000);
    });
  }
</script>
</body></html>
"@
```

### Decision detection patterns

Headings are auto-detected as decision points when they match (case-insensitive):
- `Decision N` — e.g. `### Decision 3 — Session Type`
- `AC-NNN` — acceptance criteria headings
- `US-NNN` — user story headings

Each matched heading gets ✅ Accept / ❌ Reject and a comment textarea inserted below it. Reviewed decisions get a **✓** in the TOC.

---

### Step 3 — Tell the user

After opening the browser:

> "The document is open in your browser. Accept or reject each decision point, add any comments, then click **Copy to Clipboard** in the sidebar and paste the result here."

### Step 4 — Process the pasted output

The user pastes structured text like:
```
=== Review: Spike: Simulate Command ===

Decision 1 — Operative / Weapon Input Mode
  ACCEPTED

Decision 2 — Simulate UX Style
  REJECTED
  Comment: Should support power-user args too

Decision 3 — Session Type
  ACCEPTED
  Comment: Flags are clean
```

Read each `REJECTED` / commented decision and update the relevant spec or spike doc.

---

## Usage in Spec Skill

When the spec skill invokes Human Review:
1. Read `wiki/specs/<feature>/spec.md`
2. Use this skill with title `"<Feature Name> — Spec Review"`
3. After incorporating the pasted output, return to the spec skill **Next Steps Check**

---

## Notes

- The output `<textarea>` is `readonly` — it updates only via Accept/Reject/Comment actions.
- Reviewed decisions show a **✓** in the TOC sidebar.
- `Start-Process` on Windows opens `.html` files in the system default browser.
- No server or extension required — feedback is collected by pasting into the CLI.

