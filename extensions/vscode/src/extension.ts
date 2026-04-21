import * as readline from "node:readline";
import { ChildProcessWithoutNullStreams, spawn } from "node:child_process";
import * as path from "node:path";
import * as vscode from "vscode";

type JsonRpcResponse = { jsonrpc: string; id?: string | number; result?: unknown; error?: { code: number; message: string } };
type ProviderCatalogEntry = {
    providerName: string;
    defaultModel: string;
    availableModels?: Array<{ id: string; displayName: string }>;
    localRuntimeProfiles?: Array<{ name: string; defaultChatModel: string; availableModels?: Array<{ id: string; displayName: string }> }>;
};

class AcpClient implements vscode.Disposable {
    private process: ChildProcessWithoutNullStreams | undefined;
    private initialized = false;
    private nextId = 1;
    private readonly pending = new Map<number, { resolve(value: unknown): void; reject(error: Error): void }>();

    constructor(
        private readonly output: vscode.OutputChannel,
        private readonly workspaceState: vscode.Memento,
        private readonly context: vscode.ExtensionContext) {}

    dispose(): void {
        this.process?.kill();
        this.process = undefined;
        this.pending.clear();
    }

    async call<T>(method: string, params: Record<string, unknown>): Promise<T> {
        await this.ensureStarted();
        const id = this.nextId++;
        const payload = JSON.stringify({ jsonrpc: "2.0", id, method, params });
        const response = await new Promise<unknown>((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.process!.stdin.write(payload + "\n", "utf8");
        });
        return response as T;
    }

    async ensureSession(workspaceFolder: vscode.WorkspaceFolder): Promise<string> {
        const key = `session:${workspaceFolder.uri.toString()}`;
        const existing = this.workspaceState.get<string>(key);
        if (existing) {
            try {
                await this.call("session/load", { cwd: workspaceFolder.uri.fsPath, sessionId: existing });
                return existing;
            } catch {
            }
        }

        const created = await this.call<{ sessionId: string }>("session/new", { cwd: workspaceFolder.uri.fsPath });
        await this.workspaceState.update(key, created.sessionId);
        return created.sessionId;
    }

    private async ensureStarted(): Promise<void> {
        if (this.process && this.initialized) {
            return;
        }

        const config = vscode.workspace.getConfiguration("sharpClaw");
        const command = config.get<string>("cliCommand", "dotnet");
        const args = config.get<string[]>("cliArgs", ["run", "--project", "src/SharpClaw.Code.Cli", "--", "acp"]);
        const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? this.context.extensionPath;
        this.process = spawn(command, args, { cwd, stdio: "pipe" });
        this.process.stderr.on("data", chunk => this.output.append(chunk.toString()));
        const rl = readline.createInterface({ input: this.process.stdout });
        rl.on("line", line => this.handleLine(line));
        this.process.on("exit", code => {
            for (const pending of this.pending.values()) {
                pending.reject(new Error(`SharpClaw ACP exited with code ${code ?? 0}.`));
            }
            this.pending.clear();
            this.initialized = false;
            this.process = undefined;
        });

        await this.call("initialize", { clientCapabilities: { approvalRequests: true } });
        this.initialized = true;
    }

    private handleLine(line: string): void {
        if (!line.trim()) {
            return;
        }

        const message = JSON.parse(line) as JsonRpcResponse & { method?: string; params?: any };
        if (message.method === "session/notification") {
            this.handleNotification(message.params);
            return;
        }

        if (typeof message.id !== "number") {
            return;
        }

        const pending = this.pending.get(message.id);
        if (!pending) {
            return;
        }

        this.pending.delete(message.id);
        if (message.error) {
            pending.reject(new Error(message.error.message));
            return;
        }

        pending.resolve(message.result);
    }

    private async handleNotification(params: any): Promise<void> {
        const update = params?.update;
        if (!update) {
            return;
        }

        if (update.sessionUpdate === "agentMessageChunk") {
            const text = update.chunk?.content?.text;
            if (typeof text === "string" && text.length > 0) {
                this.output.appendLine(text);
                this.output.show(true);
            }
            return;
        }

        if (update.sessionUpdate === "approvalRequest") {
            const approval = update.approval;
            const choice = await vscode.window.showWarningMessage(
                approval.prompt ?? `Approval required for ${approval.toolName}.`,
                { modal: true },
                approval.canRememberDecision ? "Allow and Remember" : "Allow",
                "Deny");
            await this.call("approval/respond", {
                requestId: approval.requestId,
                approved: choice === "Allow" || choice === "Allow and Remember",
                remember: choice === "Allow and Remember"
            });
        }
    }
}

export function activate(context: vscode.ExtensionContext): void {
    const output = vscode.window.createOutputChannel("SharpClaw Code");
    const client = new AcpClient(output, context.workspaceState, context);
    context.subscriptions.push(client, output);

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.prompt", async () => {
        const folder = requireWorkspaceFolder();
        const prompt = await vscode.window.showInputBox({ prompt: "Send a prompt to SharpClaw" });
        if (!prompt) {
            return;
        }

        const sessionId = await client.ensureSession(folder);
        const model = context.workspaceState.get<string>(`model:${folder.uri.toString()}`);
        const editor = vscode.window.activeTextEditor;
        const editorContext = editor ? buildEditorContext(editor, sessionId) : undefined;
        output.show(true);
        output.appendLine(`> ${prompt}`);
        await client.call("session/prompt", {
            cwd: folder.uri.fsPath,
            sessionId,
            model,
            prompt,
            editorContext
        });
    }));

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.refreshIndex", async () => {
        const folder = requireWorkspaceFolder();
        const result = await client.call<{ indexedFileCount: number }>("workspace/index/refresh", { cwd: folder.uri.fsPath });
        void vscode.window.showInformationMessage(`SharpClaw indexed ${result.indexedFileCount} file(s).`);
    }));

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.searchWorkspace", async () => {
        const folder = requireWorkspaceFolder();
        const query = await vscode.window.showInputBox({ prompt: "Search the indexed workspace" });
        if (!query) {
            return;
        }

        const result = await client.call<{ hits: Array<{ path: string; excerpt: string; startLine?: number }> }>("workspace/search", {
            cwd: folder.uri.fsPath,
            query,
            limit: 20
        });

        const picked = await vscode.window.showQuickPick(
            result.hits.map(hit => ({
                label: hit.path,
                description: hit.startLine ? `Line ${hit.startLine}` : undefined,
                detail: hit.excerpt,
                hit
            })),
            { placeHolder: "Workspace search results" });
        if (!picked) {
            return;
        }

        const target = vscode.Uri.file(path.join(folder.uri.fsPath, picked.hit.path));
        const document = await vscode.workspace.openTextDocument(target);
        const editor = await vscode.window.showTextDocument(document);
        if (picked.hit.startLine && picked.hit.startLine > 0) {
            const line = Math.max(0, picked.hit.startLine - 1);
            const position = new vscode.Position(line, 0);
            editor.selection = new vscode.Selection(position, position);
            editor.revealRange(new vscode.Range(position, position));
        }
    }));

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.saveMemory", async () => {
        const folder = requireWorkspaceFolder();
        const editor = vscode.window.activeTextEditor;
        const selectionText = editor && !editor.selection.isEmpty ? editor.document.getText(editor.selection) : "";
        const content = await vscode.window.showInputBox({
            prompt: "Memory content",
            value: selectionText
        });
        if (!content) {
            return;
        }

        const scope = await vscode.window.showQuickPick(["project", "user"], { placeHolder: "Memory scope" });
        if (!scope) {
            return;
        }

        await client.call("memory/save", {
            cwd: folder.uri.fsPath,
            sessionId: await client.ensureSession(folder),
            request: {
                scope,
                content,
                source: editor ? "vscode-selection" : "vscode-manual",
                relatedFilePath: editor ? vscode.workspace.asRelativePath(editor.document.uri, false) : undefined
            }
        });
        void vscode.window.showInformationMessage(`Saved ${scope} memory.`);
    }));

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.listMemory", async () => {
        const folder = requireWorkspaceFolder();
        const result = await client.call<Array<{ id: string; scope: string; content: string }>>("memory/list", {
            cwd: folder.uri.fsPath,
            limit: 30
        });
        await vscode.window.showQuickPick(
            result.map(entry => ({
                label: entry.id,
                description: entry.scope,
                detail: entry.content
            })),
            { placeHolder: "Saved SharpClaw memory" });
    }));

    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.selectModel", async () => {
        const folder = requireWorkspaceFolder();
        const providers = await client.call<ProviderCatalogEntry[]>("models/list", {});
        const picks = providers.flatMap(provider => {
            const base = [{
                label: provider.defaultModel,
                description: provider.providerName,
                value: provider.defaultModel
            }];
            const profilePicks = (provider.localRuntimeProfiles ?? []).flatMap(profile => {
                const discovered = profile.availableModels ?? [];
                if (discovered.length === 0) {
                    return [{
                        label: `${profile.name}/${profile.defaultChatModel}`,
                        description: provider.providerName,
                        value: `${profile.name}/${profile.defaultChatModel}`
                    }];
                }

                return discovered.map(model => ({
                    label: `${profile.name}/${model.id}`,
                    description: provider.providerName,
                    value: `${profile.name}/${model.id}`
                }));
            });
            return [...base, ...profilePicks];
        });
        const selected = await vscode.window.showQuickPick(picks, { placeHolder: "Select the model for ACP prompts" });
        if (!selected) {
            return;
        }

        await context.workspaceState.update(`model:${folder.uri.toString()}`, selected.value);
        void vscode.window.showInformationMessage(`SharpClaw model set to ${selected.value}.`);
    }));
}

export function deactivate(): void {
}

function requireWorkspaceFolder(): vscode.WorkspaceFolder {
    const folder = vscode.workspace.workspaceFolders?.[0];
    if (!folder) {
        throw new Error("Open a workspace folder before using SharpClaw.");
    }

    return folder;
}

function buildEditorContext(editor: vscode.TextEditor, sessionId: string): Record<string, unknown> {
    const selection = editor.selection;
    return {
        workspaceRoot: vscode.workspace.getWorkspaceFolder(editor.document.uri)?.uri.fsPath,
        currentFilePath: editor.document.uri.fsPath,
        selection: selection.isEmpty
            ? undefined
            : {
                start: editor.document.offsetAt(selection.start),
                end: editor.document.offsetAt(selection.end),
                text: editor.document.getText(selection)
            },
        sessionId
    };
}
