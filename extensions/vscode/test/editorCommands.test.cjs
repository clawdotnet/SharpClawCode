const { test } = require("node:test");
const assert = require("node:assert/strict");
const Module = require("node:module");

test("editor commands steer and cancel the prompt they started and display unapplied corrections", { timeout: 10000 }, async t => {
    const commands = new Map(), lines = [], saved = new Map();
    let signalProgress;
    const progress = new Promise(resolve => { signalProgress = resolve; });
    const fixture = `
const io = require("node:readline").createInterface({input: process.stdin});
let prompt, text;
const send = payload => console.log(JSON.stringify({jsonrpc:"2.0", ...payload}));
io.on("line", line => {
    const m = JSON.parse(line);
    if (m.method === "initialize") send({id:m.id,result:{agentCapabilities:{liveTurnControl:true}}});
    if (m.method === "session/new") send({id:m.id,result:{sessionId:"editor-session"}});
    if (m.method === "session/prompt" && m.params.liveTurnControl) {
        prompt = m;
        send({method:"session/notification",params:{sessionId:m.params.sessionId,promptRequestId:m.id,
            update:{sessionUpdate:"turnProgress",kind:"toolStarted",toolName:"read_file"}}});
    }
    if (m.method === "session/steer" || m.method === "session/cancel") {
        const matches = m.params.promptRequestId === prompt.id && m.params.sessionId === prompt.params.sessionId && m.params.cwd === prompt.params.cwd;
        send({id:m.id,result:{accepted:matches}});
        if (matches && m.method === "session/steer") text = m.params.text;
        if (matches && m.method === "session/cancel") send({id:prompt.id,result:{stopReason:"cancelled",pendingSteering:[text]}});
    }
});
`;
    const folder = { uri: { fsPath: process.cwd(), toString: () => "workspace" } };
    const output = {
        show() {}, dispose() {}, append() {},
        appendLine(line) { lines.push(line); if (line.includes("Running read_file")) signalProgress(); }
    };
    const vscode = {
        commands: { registerCommand(id, callback) { commands.set(id, callback); return { dispose() {} }; } },
        workspace: {
            workspaceFolders: [folder],
            getConfiguration() { return { get(key) { return key === "cliCommand" ? process.execPath : ["-e", fixture]; } }; }
        },
        window: {
            createOutputChannel: () => output,
            showInputBox: async options => options.prompt.includes("Correction") ? "Preserve the API" : "Inspect",
            showInformationMessage: async () => {}
        }
    };
    const load = Module._load;
    let activate;
    try {
        Module._load = function(name, ...args) { return name === "vscode" ? vscode : load.call(this, name, ...args); };
        ({activate} = require("../out/extension.js"));
    } finally { Module._load = load; }
    const subscriptions = [];
    t.after(() => subscriptions.forEach(item => item.dispose()));
    activate({subscriptions, extensionPath:process.cwd(), workspaceState:{
        get: key => saved.get(key), update: async (key, value) => saved.set(key, value)
    }});
    const prompt = commands.get("sharpClaw.prompt")();
    await progress;
    await commands.get("sharpClaw.steerTurn")();
    await commands.get("sharpClaw.cancelTurn")();
    await prompt;
    assert.ok(lines.includes("Correction queued."));
    assert.ok(lines.includes("Cancellation requested."));
    assert.ok(lines.includes("Turn cancelled."));
    assert.ok(lines.includes("Unapplied correction: Preserve the API"));
});
