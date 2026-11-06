import { ChildProcessWithoutNullStreams } from "node:child_process";
import * as readline from "node:readline";

type RpcId = number | string;
type Params = Record<string, unknown>;
type Reply = (method: string, params: Params) => Promise<unknown>;

export class AcpError extends Error {
    constructor(message: string, readonly data?: { pendingSteering?: string[] }) { super(message); }
}

/** One initialized subprocess owns every pending request and notification reply on its connection. */
export class AcpConnection {
    private child?: ChildProcessWithoutNullStreams;
    private startup?: Promise<boolean>;
    private disposed = false;
    private nextId = 1;
    private readonly pending = new Map<RpcId, { resolve(value: unknown): void; reject(error: Error): void }>();

    constructor(private readonly spawnHost: () => ChildProcessWithoutNullStreams,
        private readonly notify: (params: any, reply: Reply) => Promise<void>, private readonly log: (text: string) => void) {}

    capabilities(): Promise<boolean> {
        if (this.disposed) return Promise.reject(new Error("The ACP connection is closed."));
        if (!this.startup) {
            const starting = this.start();
            this.startup = starting;
            void starting.catch(() => { if (this.startup === starting) this.startup = undefined; });
        }
        return this.startup;
    }

    private async start(): Promise<boolean> {
        const child = this.spawnHost();
        this.child = child;
        child.stderr.on("data", chunk => this.log(chunk.toString()));
        child.once("error", error => this.close(child, error));
        child.once("close", code => this.close(child, new Error(`SharpClaw ACP exited with code ${code ?? "unknown"}.`)));
        child.stdin.on("error", error => this.close(child, error));
        const lines = readline.createInterface({ input: child.stdout });
        lines.on("line", line => {
            if (this.child !== child || !line.trim()) return;
            let message;
            try { message = JSON.parse(line); }
            catch { this.log(line + "\n"); return; }
            if (message?.method === "session/notification") {
                void this.notify(message.params, (method, params) => this.request(child, method, params))
                    .catch(error => this.log(String(error) + "\n"));
                return;
            }
            const pending = this.pending.get(message?.id);
            if (!pending) return;
            this.pending.delete(message.id);
            if (message.error) pending.reject(new AcpError(message.error.message, message.error.data));
            else pending.resolve(message.result);
        });
        try {
            const init = await this.request(child, "initialize", { clientCapabilities: { approvalRequests: true } }) as
                { agentCapabilities?: { liveTurnControl?: boolean } };
            return init.agentCapabilities?.liveTurnControl === true;
        } catch (error) {
            this.close(child, error instanceof Error ? error : new Error(String(error)));
            throw error;
        }
    }

    async call<T>(method: string, params: Params, id?: RpcId): Promise<T> {
        await this.capabilities();
        if (!this.child) throw new Error("The ACP connection is closed.");
        return await this.request(this.child, method, params, id) as T;
    }

    private request(child: ChildProcessWithoutNullStreams, method: string, params: Params, id: RpcId = this.nextId++): Promise<unknown> {
        return new Promise((resolve, reject) => {
            if (child !== this.child) { reject(new Error("The ACP connection is closed.")); return; }
            this.pending.set(id, { resolve, reject });
            child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n", error => {
                if (error && this.child === child) this.close(child, error);
            });
        });
    }

    private close(child: ChildProcessWithoutNullStreams, error: Error, terminate = true): void {
        if (this.child !== child) return;
        this.child = undefined;
        this.startup = undefined;
        for (const request of this.pending.values()) request.reject(error);
        this.pending.clear();
        if (terminate && child.exitCode === null && !child.killed) child.kill();
    }

    dispose(): void {
        this.disposed = true;
        const child = this.child;
        if (!child) return;
        this.close(child, new Error("The ACP connection was disposed."), false);
        // EOF lets the host settle its live turns and approvals during extension shutdown.
        child.stdin.end();
        const deadline = setTimeout(() => child.kill(), 10_000);
        deadline.unref();
        child.once("close", () => clearTimeout(deadline));
    }
}
