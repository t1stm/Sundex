// Starts sundex-style-ls for .snx.ss files. The grammar and language settings need no code.
const vscode = require("vscode");
const { LanguageClient } = require("vscode-languageclient/node");

let client;

exports.activate = () => {
    const settings = vscode.workspace.getConfiguration("sundexStyle.server");
    const server = { command: settings.get("command"), args: settings.get("args") };
    client = new LanguageClient("sundexStyle", "Sundex Style", { run: server, debug: server }, {
        documentSelector: [{ language: "sundex-style" }]
    });
    return client.start();
};

exports.deactivate = () => client?.stop();
