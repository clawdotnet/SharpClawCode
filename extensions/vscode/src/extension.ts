import { randomUUID } from "node:crypto";
import { AcpConnection, AcpError } from "./acpConnection";
import { spawn } from "node:child_process";
import * as path from "node:path";
import * as vscode from "vscode";

type ProviderCatalogEntry = {
    providerName: string;
    defaultModel: string;
    availableModels?: Array<{ id: string; displayName: string }>;
    localRuntimeProfiles?: Array<{ name: string; defaultChatModel: string; availableModels?: Array<{ id: string; displayName: string }> }>;
};

class AcpClient implements vscode.Disposable {
    private readonly connection: AcpConnection;
    private readonly busy = new Set<string>();
    private readonly active = new Map<string, { cwd: string; sessionId: string; promptRequestId: string }>();

    constructor(private readonly output: vscode.OutputChannel, private readonly workspaceState: vscode.Memento,
        context: vscode.ExtensionContext) {
        this.connection = new AcpConnection(() => {
            const config = vscode.workspace.getConfiguration("sharpClaw");
            if (!vscode.workspace.isTrusted) throw new Error("SharpClaw requires a trusted workspace.");
            const command = config.inspect<string>("cliCommand")?.globalValue ?? "sharpclaw";
            const args = config.inspect<string[]>("cliArgs")?.globalValue ?? ["acp"];
            const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? context.extensionPath;
            return spawn(command, args, { cwd, stdio: "pipe" });
        }, (params, reply) => this.handleNotification(params, reply), text => this.output.append(text));
    }

    dispose(): void { this.connection.dispose(); }

    call<T>(method: string, params: Record<string, unknown>): Promise<T> {
        return this.connection.call<T>(method, params);
    }

    async prompt(folder: vscode.WorkspaceFolder, prompt: string): Promise<void> {
        const key = folder.uri.toString();
        if (this.busy.has(key)) {
            void vscode.window.showInformationMessage("A SharpClaw prompt is running. Use Steer Active Turn to add a correction.");
            return;
        }
        this.busy.add(key);
        try {
            const sessionId = await this.ensureSession(folder);
            const live = await this.connection.capabilities();
            const target = { cwd: folder.uri.fsPath, sessionId, promptRequestId: randomUUID() };
            if (live) this.active.set(key, target);
            const editor = vscode.window.activeTextEditor;
            const editorContext = editor && vscode.workspace.getWorkspaceFolder(editor.document.uri)?.uri.toString() === key
                ? buildEditorContext(editor, sessionId) : undefined;
            this.output.show(true);
            this.output.appendLine(`> ${prompt}`);
            const result = await this.connection.call<{ stopReason: string; pendingSteering?: string[] }>("session/prompt", {
                cwd: target.cwd, sessionId, prompt, model: this.workspaceState.get<string>(`model:${key}`),
                editorContext, ...(live ? { liveTurnControl: true } : {})
            }, target.promptRequestId);
            if (result.stopReason === "cancelled") this.output.appendLine("Turn cancelled.");
            this.showPending(result.pendingSteering);
        } catch (error) {
            if (error instanceof AcpError) this.showPending(error.data?.pendingSteering);
            throw error;
        } finally {
            this.active.delete(key);
            this.busy.delete(key);
        }
    }

    async control(folder: vscode.WorkspaceFolder, cancel: boolean): Promise<void> {
        const target = this.active.get(folder.uri.toString());
        if (!target) {
            void vscode.window.showInformationMessage("No active SharpClaw turn with live controls in this workspace.");
            return;
        }
        const text = cancel ? undefined : await vscode.window.showInputBox({ prompt: "Correction for the active SharpClaw turn" });
        if (!cancel && !text?.trim()) return;
        const result = await this.connection.call<{ accepted: boolean }>(cancel ? "session/cancel" : "session/steer", { ...target, text });
        this.output.show(true);
        this.output.appendLine(result.accepted ? (cancel ? "Cancellation requested." : "Correction queued.")
            : "The turn could not accept this request.");
    }

    private showPending(messages?: string[]): void {
        for (const message of messages ?? []) this.output.appendLine(`Unapplied correction: ${message}`);
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

    private async handleNotification(params: any, reply: (method: string, params: Record<string, unknown>) => Promise<unknown>): Promise<void> {
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

        if (update.sessionUpdate === "turnProgress") {
            const target = [...this.active.values()].find(item => item.promptRequestId === params.promptRequestId && item.sessionId === params.sessionId);
            if (!target) return;
            const descriptions: Record<string, string> = {
                turnStarted: "Turn started", toolStarted: `Running ${update.toolName}`,
                toolCompleted: `${update.toolName} ${update.succeeded ? "completed" : "failed"}`,
                contextCompacted: "Context compacted", steeringAccepted: "Correction accepted"
            };
            if (descriptions[update.kind]) this.output.appendLine(`[${path.basename(target.cwd)}] ${descriptions[update.kind]}`);
            return;
        }

        if (update.sessionUpdate === "approvalRequest") {
            const approval = update.approval;
            const choice = await vscode.window.showWarningMessage(
                approval.prompt ?? `Approval required for ${approval.toolName}.`,
                { modal: true },
                approval.canRememberDecision ? "Allow and Remember" : "Allow",
                "Deny");
            await reply("approval/respond", {
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

        await client.prompt(folder, prompt);
    }));
    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.cancelTurn",
        () => client.control(requireWorkspaceFolder(), true)));
    context.subscriptions.push(vscode.commands.registerCommand("sharpClaw.steerTurn",
        () => client.control(requireWorkspaceFolder(), false)));

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
    const editor = vscode.window.activeTextEditor;
    const folder = (editor && vscode.workspace.getWorkspaceFolder(editor.document.uri)) ?? vscode.workspace.workspaceFolders?.[0];
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
