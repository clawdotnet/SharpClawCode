const { test } = require("node:test");
const assert = require("node:assert/strict");
const { spawn } = require("node:child_process");
const { AcpConnection } = require("../out/acpConnection.js");

const script = `
const lines = require("node:readline").createInterface({input:process.stdin});
let initialized = false, prompt;
function send(payload) { console.log(JSON.stringify({jsonrpc:"2.0", ...payload})); }
lines.on("line", line => {
    const msg = JSON.parse(line);
    if (msg.method === "initialize") {
        if (initialized) process.exit(2);
        initialized = true;
        send({id:msg.id, result:{agentCapabilities:{liveTurnControl:true}}});
    } else if (!initialized) process.exit(3);
    else if (msg.method === "session/prompt") {
        prompt = msg.id;
        send({method:"session/notification",params:{promptRequestId:prompt,update:{sessionUpdate:"turnProgress"}}});
    } else if (msg.method === "session/cancel") {
        send({id:msg.id,result:{accepted:msg.params.promptRequestId === prompt}});
        send({id:prompt,result:{stopReason:"cancelled", pendingSteering:["late correction"]}});
    } else if (msg.method === "exit") process.exit(1);
    else send({id:msg.id,result:msg.params});
});
`;

test("concurrent requests share one initialized host, including controls while a prompt is pending", {timeout:10000}, async t => {
    let starts = 0, notify;
    const progress = new Promise(resolve => { notify = resolve; });
    const connection = new AcpConnection(() => { starts++; return spawn(process.execPath, ["-e", script]); },
        async params => notify(params), () => {});
    t.after(() => connection.dispose());
    const results = await Promise.all([connection.call("echo", {n:1}), connection.call("echo", {n:2})]);
    assert.deepEqual(results, [{n:1}, {n:2}]);
    assert.equal(await connection.capabilities(), true);
    const prompt = connection.call("session/prompt", {}, "prompt-1");
    assert.equal((await progress).promptRequestId, "prompt-1");
    assert.deepEqual(await connection.call("session/cancel", {promptRequestId:"prompt-1"}), {accepted:true});
    assert.deepEqual(await prompt, {stopReason:"cancelled", pendingSteering:["late correction"]});
    assert.equal(starts, 1);
});

test("exit rejects pending requests and stale notification replies cannot reach a replacement host", {timeout:10000}, async t => {
    let notify;
    const notification = new Promise(resolve => { notify = resolve; });
    const connection = new AcpConnection(() => spawn(process.execPath, ["-e", script]),
        async (_, reply) => notify(reply), () => {});
    t.after(() => connection.dispose());
    const pending = connection.call("session/prompt", {}, "old");
    const oldReply = await notification;
    await Promise.all([assert.rejects(pending, /exited/), assert.rejects(connection.call("exit", {}), /exited/)]);
    assert.deepEqual(await connection.call("echo", {fresh:true}), {fresh:true});
    await assert.rejects(oldReply("approval/respond", {approved:true}), /closed/);
});

test("disposal releases a pending prompt and prevents another process from starting", {timeout:10000}, async () => {
    let notify;
    const started = new Promise(resolve => { notify = resolve; });
    const connection = new AcpConnection(() => spawn(process.execPath, ["-e", script]), async () => notify(), () => {});
    const pending = connection.call("session/prompt", {}, "pending");
    await started;
    connection.dispose();
    await assert.rejects(pending, /disposed/);
    await assert.rejects(connection.call("echo", {}), /closed/);
});
