const state = {
    session: null,
    exports: [],
    graph: null,
    library: { templates: [] },
    selectedNodeId: null,
    selectedTemplateKey: null,
    pendingPin: null,
    drag: null
};

const els = {
    engineVersion: document.getElementById("engineVersionSelect"),
    assetFiles: document.getElementById("assetFilesInput"),
    assetDirectory: document.getElementById("assetDirectoryInput"),
    usmap: document.getElementById("usmapInput"),
    open: document.getElementById("openAssetButton"),
    exportSelect: document.getElementById("exportSelect"),
    refreshGraph: document.getElementById("refreshGraphButton"),
    reloadLibrary: document.getElementById("reloadLibraryButton"),
    libraryFilter: document.getElementById("libraryFilter"),
    libraryList: document.getElementById("libraryList"),
    nodeLayer: document.getElementById("nodeLayer"),
    wireLayer: document.getElementById("wireLayer"),
    status: document.getElementById("statusText"),
    addInt: document.getElementById("addIntButton"),
    addString: document.getElementById("addStringButton"),
    addBool: document.getElementById("addBoolButton"),
    autoLayout: document.getElementById("autoLayoutButton"),
    deleteNode: document.getElementById("deleteNodeButton"),
    apply: document.getElementById("applyGraphButton"),
    save: document.getElementById("saveAssetButton"),
    inspectorEmpty: document.getElementById("inspectorEmpty"),
    inspectorForm: document.getElementById("inspectorForm"),
    nodeTitle: document.getElementById("nodeTitleInput"),
    nodeOwner: document.getElementById("nodeOwnerInput"),
    nodeFunction: document.getElementById("nodeFunctionInput"),
    nodeExpression: document.getElementById("nodeExpressionInput"),
    nodeValue: document.getElementById("nodeValueInput"),
    nodePayload: document.getElementById("nodePayloadInput"),
    updateNode: document.getElementById("updateNodeButton"),
    templateEditorEmpty: document.getElementById("templateEditorEmpty"),
    templateEditorForm: document.getElementById("templateEditorForm"),
    templateName: document.getElementById("templateNameInput"),
    templateComment: document.getElementById("templateCommentInput"),
    pinCommentList: document.getElementById("pinCommentList"),
    saveTemplateComment: document.getElementById("saveTemplateCommentButton"),
    addTemplateNode: document.getElementById("addTemplateNodeButton"),
    messages: document.getElementById("messages")
};

els.open.addEventListener("click", openAsset);
els.exportSelect.addEventListener("change", loadSelectedGraph);
els.refreshGraph.addEventListener("click", loadSelectedGraph);
els.reloadLibrary.addEventListener("click", loadLibrary);
els.libraryFilter.addEventListener("input", renderLibrary);
els.addInt.addEventListener("click", () => addNode("EX_IntConst", "整数", { value: 0 }));
els.addString.addEventListener("click", () => addNode("EX_StringConst", "字符串", { value: "" }));
els.addBool.addEventListener("click", () => addNode("EX_True", "True", { value: true }));
els.autoLayout.addEventListener("click", autoLayoutNodes);
els.deleteNode.addEventListener("click", deleteSelectedNode);
els.apply.addEventListener("click", applyGraph);
els.save.addEventListener("click", saveAsset);
els.inspectorForm.addEventListener("submit", updateSelectedNode);
els.templateEditorForm.addEventListener("submit", saveSelectedTemplateComments);
els.addTemplateNode.addEventListener("click", () => {
    const template = getSelectedTemplate();
    if (template) addTemplateNode(template);
});

window.addEventListener("resize", drawWires);

loadLibrary();

async function openAsset() {
    const selectedFiles = collectSelectedFiles();
    const uasset = selectedFiles.find(file => file.name.toLowerCase().endsWith(".uasset"));
    if (!uasset) {
        addMessage("缺少文件", "请先选择一个 .uasset 文件。");
        return;
    }

    const form = new FormData();
    form.append("engineVersion", els.engineVersion.value);
    form.append("uasset", uasset, uasset.name);
    for (const file of selectedFiles) {
        if (file === uasset) continue;
        form.append("files", file, file.webkitRelativePath || file.name);
    }
    if (els.usmap.files[0]) form.append("usmap", els.usmap.files[0], els.usmap.files[0].name);

    setStatus("正在打开资产...");
    const result = await postForm("/api/assets/open", form);
    state.session = result.sessionId;
    state.exports = result.exports ?? [];
    renderExports();
    addMessages(result.warnings ?? []);
    setStatus(`${result.assetName} 已打开`);

    const firstScript = state.exports.find(item => item.hasScriptBytecode);
    if (firstScript) {
        els.exportSelect.value = String(firstScript.exportIndex);
        await loadSelectedGraph();
    }
}

async function loadSelectedGraph() {
    if (!state.session || !els.exportSelect.value) return;
    setStatus("正在加载节点图...");
    state.graph = await getJson(`/api/assets/${state.session}/graphs/${els.exportSelect.value}`);
    state.selectedNodeId = null;
    renderGraph();
    addMessages(state.graph.warnings ?? []);
    setStatus(`${state.graph.exportName} 已加载`);
}

async function applyGraph() {
    if (!state.session || !state.graph) return;
    syncConnectionsFromPins();
    setStatus("正在应用节点修改...");
    const result = await putJson(`/api/assets/${state.session}/graphs/${state.graph.exportIndex}`, state.graph);
    addMessages(result.warnings ?? []);
    if (result.unresolvedNodeIds?.length) {
        addMessage("未解析节点", result.unresolvedNodeIds.join(", "));
    }
    setStatus(`已编译 ${result.compiledExpressionCount} 个表达式`);
    await loadLibrary();
}

async function saveAsset() {
    if (!state.session) return;
    setStatus("正在保存资产...");
    const result = await postJson(`/api/assets/${state.session}/save`, {});
    addMessages(result.warnings ?? []);
    addMessage("已保存", `${result.outputPath}（备份：${result.backupPath}）`);
    setStatus("资产已保存");
}

function collectSelectedFiles() {
    const files = [];
    for (const file of els.assetFiles.files ?? []) files.push(file);
    for (const file of els.assetDirectory.files ?? []) files.push(file);
    return files;
}

async function loadLibrary() {
    state.library = await getJson("/api/node-library");
    renderLibrary();
    renderTemplateEditor();
}

function renderExports() {
    els.exportSelect.replaceChildren();
    for (const item of state.exports) {
        const option = document.createElement("option");
        option.value = item.exportIndex;
        option.textContent = `${item.exportIndex}: ${item.name} ${item.hasScriptBytecode ? `(${item.scriptNodeCount})` : ""}`;
        els.exportSelect.append(option);
    }
}

function renderGraph() {
    els.nodeLayer.replaceChildren();
    els.wireLayer.replaceChildren();

    if (!state.graph) return;
    fitCanvasToNodes();
    for (const node of state.graph.nodes) {
        renderNode(node);
    }
    drawWires();
    renderInspector();
}

function renderNode(node) {
    const element = document.createElement("article");
    element.className = `node ${node.kind.toLowerCase()} ${state.selectedNodeId === node.id ? "selected" : ""}`;
    element.dataset.nodeId = node.id;
    element.style.left = `${node.position?.x ?? 0}px`;
    element.style.top = `${node.position?.y ?? 0}px`;

    const header = document.createElement("div");
    header.className = "node-header";
    header.textContent = node.title || node.expressionType || "节点";
    header.addEventListener("pointerdown", event => startDrag(event, node));
    element.append(header);

    const body = document.createElement("div");
    body.className = "node-body";
    const inputs = document.createElement("div");
    const outputs = document.createElement("div");

    for (const pin of node.pins ?? []) {
        const pinEl = document.createElement("div");
        pinEl.className = `pin ${pin.direction.toLowerCase()}`;
        pinEl.dataset.pinId = pin.id;
        pinEl.dataset.nodeId = node.id;

        const dot = document.createElement("span");
        dot.className = "pin-dot";
        dot.title = `${pin.name}: ${pin.pinType}${pin.comment ? `\n${pin.comment}` : ""}`;
        dot.addEventListener("click", event => {
            event.stopPropagation();
            clickPin(node, pin, dot);
        });

        const label = document.createElement("span");
        label.textContent = pin.name;
        pinEl.append(dot, label);
        (pin.direction === "Output" ? outputs : inputs).append(pinEl);
    }

    body.append(inputs, outputs);
    element.append(body);
    element.addEventListener("click", () => selectNode(node.id));
    els.nodeLayer.append(element);
}

function startDrag(event, node) {
    event.preventDefault();
    selectNode(node.id);
    state.drag = {
        id: node.id,
        startX: event.clientX,
        startY: event.clientY,
        nodeX: node.position.x,
        nodeY: node.position.y
    };
    window.addEventListener("pointermove", dragMove);
    window.addEventListener("pointerup", stopDrag, { once: true });
}

function dragMove(event) {
    if (!state.drag || !state.graph) return;
    const node = state.graph.nodes.find(item => item.id === state.drag.id);
    if (!node) return;
    node.position.x = Math.max(0, state.drag.nodeX + event.clientX - state.drag.startX);
    node.position.y = Math.max(0, state.drag.nodeY + event.clientY - state.drag.startY);
    const element = els.nodeLayer.querySelector(`[data-node-id="${CSS.escape(node.id)}"]`);
    if (element) {
        element.style.left = `${node.position.x}px`;
        element.style.top = `${node.position.y}px`;
    }
    drawWires();
}

function stopDrag() {
    state.drag = null;
    window.removeEventListener("pointermove", dragMove);
}

function clickPin(node, pin, dot) {
    if (!state.pendingPin) {
        state.pendingPin = { node, pin };
        dot.classList.add("active");
        return;
    }

    const first = state.pendingPin;
    clearActivePins();
    state.pendingPin = null;

    if (first.node.id === node.id || first.pin.direction === pin.direction) {
        return;
    }

    const output = first.pin.direction === "Output" ? first : { node, pin };
    const input = first.pin.direction === "Input" ? first : { node, pin };
    connectPins(output.node, output.pin, input.node, input.pin);
    drawWires();
}

function connectPins(fromNode, fromPin, toNode, toPin) {
    fromPin.linkedTo ??= [];
    toPin.linkedTo ??= [];
    if (!fromPin.linkedTo.includes(toPin.id)) fromPin.linkedTo.push(toPin.id);
    if (!toPin.linkedTo.includes(fromPin.id)) toPin.linkedTo.push(fromPin.id);
    state.graph.connections ??= [];
    if (!state.graph.connections.some(item => item.fromPinId === fromPin.id && item.toPinId === toPin.id)) {
        state.graph.connections.push({ fromNodeId: fromNode.id, fromPinId: fromPin.id, toNodeId: toNode.id, toPinId: toPin.id });
    }
}

function drawWires() {
    els.wireLayer.replaceChildren();
    if (!state.graph) return;

    syncConnectionsFromPins();
    for (const connection of state.graph.connections ?? []) {
        const from = getPinCenter(connection.fromNodeId, connection.fromPinId);
        const to = getPinCenter(connection.toNodeId, connection.toPinId);
        if (!from || !to) continue;

        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        const dx = Math.max(80, Math.abs(to.x - from.x) * 0.45);
        path.setAttribute("class", "wire");
        path.setAttribute("d", `M ${from.x} ${from.y} C ${from.x + dx} ${from.y}, ${to.x - dx} ${to.y}, ${to.x} ${to.y}`);
        els.wireLayer.append(path);
    }
}

function getPinCenter(nodeId, pinId) {
    const nodeEl = els.nodeLayer.querySelector(`[data-node-id="${CSS.escape(nodeId)}"]`);
    const pinEl = els.nodeLayer.querySelector(`[data-pin-id="${CSS.escape(pinId)}"] .pin-dot`);
    if (!nodeEl || !pinEl) return null;
    const nodeRect = els.nodeLayer.getBoundingClientRect();
    const pinRect = pinEl.getBoundingClientRect();
    return {
        x: pinRect.left - nodeRect.left + pinRect.width / 2,
        y: pinRect.top - nodeRect.top + pinRect.height / 2
    };
}

function selectNode(id) {
    state.selectedNodeId = id;
    for (const element of els.nodeLayer.querySelectorAll(".node")) {
        element.classList.toggle("selected", element.dataset.nodeId === id);
    }
    renderInspector();
}

function renderInspector() {
    const node = getSelectedNode();
    els.inspectorEmpty.classList.toggle("hidden", Boolean(node));
    els.inspectorForm.classList.toggle("hidden", !node);
    if (!node) return;

    els.nodeTitle.value = node.title ?? "";
    els.nodeOwner.value = node.ownerKey ?? "";
    els.nodeFunction.value = node.functionName ?? "";
    els.nodeExpression.value = node.expressionType ?? "";
    els.nodeValue.value = node.payload?.value ?? "";
    els.nodePayload.value = JSON.stringify(node.payload ?? {}, null, 2);
}

function updateSelectedNode(event) {
    event.preventDefault();
    const node = getSelectedNode();
    if (!node) return;

    node.title = els.nodeTitle.value;
    node.ownerKey = els.nodeOwner.value;
    node.functionName = els.nodeFunction.value;
    node.expressionType = els.nodeExpression.value;
    try {
        node.payload = JSON.parse(els.nodePayload.value || "{}");
        if (els.nodeValue.value !== "") {
            node.payload.value = coerceValue(els.nodeValue.value);
        }
    } catch (error) {
        addMessage("Payload 无效", error.message);
        return;
    }

    renderGraph();
}

function addNode(expressionType, title, payload) {
    if (!state.graph) return;
    const node = {
        id: `new-${crypto.randomUUID()}`,
        kind: "Expression",
        title,
        ownerKey: "",
        functionName: "",
        expressionType,
        position: { x: 140, y: 140 },
        payload,
        pins: [
            { id: `new-${crypto.randomUUID()}:Input:In`, name: "In", direction: "Input", pinType: "Exec", linkedTo: [] },
            { id: `new-${crypto.randomUUID()}:Output:Out`, name: "Out", direction: "Output", pinType: "Exec", linkedTo: [] },
            { id: `new-${crypto.randomUUID()}:Output:Value`, name: "Value", direction: "Output", pinType: expressionType.replace("EX_", ""), linkedTo: [] }
        ]
    };
    state.graph.nodes.push(node);
    selectNode(node.id);
    renderGraph();
}

function deleteSelectedNode() {
    if (!state.graph || !state.selectedNodeId) return;
    const removed = state.selectedNodeId;
    state.graph.nodes = state.graph.nodes.filter(node => node.id !== removed);
    for (const node of state.graph.nodes) {
        for (const pin of node.pins ?? []) {
            pin.linkedTo = (pin.linkedTo ?? []).filter(pinId => !pinId.startsWith(`${removed}:`));
        }
    }
    state.graph.connections = (state.graph.connections ?? []).filter(item => item.fromNodeId !== removed && item.toNodeId !== removed);
    state.selectedNodeId = null;
    renderGraph();
}

function autoLayoutNodes() {
    if (!state.graph?.nodes?.length) return;
    syncConnectionsFromPins();

    const nodes = state.graph.nodes;
    const nodeMap = new Map(nodes.map(node => [node.id, node]));
    const incoming = new Map(nodes.map(node => [node.id, 0]));
    const outgoing = new Map(nodes.map(node => [node.id, []]));

    for (const connection of state.graph.connections ?? []) {
        if (!nodeMap.has(connection.fromNodeId) || !nodeMap.has(connection.toNodeId)) continue;
        outgoing.get(connection.fromNodeId).push(connection.toNodeId);
        incoming.set(connection.toNodeId, (incoming.get(connection.toNodeId) ?? 0) + 1);
    }

    const layerByNode = new Map();
    const queue = nodes
        .filter(node => (incoming.get(node.id) ?? 0) === 0)
        .map(node => node.id);

    if (!queue.length) {
        queue.push(...nodes.map(node => node.id));
    }

    for (const id of queue) {
        layerByNode.set(id, 0);
    }

    while (queue.length) {
        const id = queue.shift();
        const currentLayer = layerByNode.get(id) ?? 0;
        for (const next of outgoing.get(id) ?? []) {
            const nextLayer = Math.max(layerByNode.get(next) ?? 0, currentLayer + 1);
            if (nextLayer !== layerByNode.get(next)) {
                layerByNode.set(next, nextLayer);
                queue.push(next);
            }
        }
    }

    for (const node of nodes) {
        if (!layerByNode.has(node.id)) {
            layerByNode.set(node.id, 0);
        }
    }

    const layers = new Map();
    for (const node of nodes) {
        const layer = layerByNode.get(node.id) ?? 0;
        if (!layers.has(layer)) layers.set(layer, []);
        layers.get(layer).push(node);
    }

    const columnGap = 320;
    const rowGap = 170;
    const startX = 90;
    const startY = 80;
    for (const [layer, layerNodes] of [...layers.entries()].sort((a, b) => a[0] - b[0])) {
        layerNodes
            .sort((a, b) => (a.title || a.id).localeCompare(b.title || b.id))
            .forEach((node, row) => {
                node.position.x = startX + layer * columnGap;
                node.position.y = startY + row * rowGap;
            });
    }

    fitCanvasToNodes();
    renderGraph();
    setStatus("节点已自动整理");
}

function fitCanvasToNodes() {
    if (!state.graph?.nodes?.length) return;
    const maxX = Math.max(...state.graph.nodes.map(node => node.position.x)) + 420;
    const maxY = Math.max(...state.graph.nodes.map(node => node.position.y)) + 260;
    els.nodeLayer.style.width = `${Math.max(1800, maxX)}px`;
    els.nodeLayer.style.height = `${Math.max(1200, maxY)}px`;
    els.wireLayer.style.width = els.nodeLayer.style.width;
    els.wireLayer.style.height = els.nodeLayer.style.height;
}

function renderLibrary() {
    const filter = els.libraryFilter.value.trim().toLowerCase();
    els.libraryList.replaceChildren();
    for (const template of state.library.templates ?? []) {
        if (filter && !`${template.ownerKey} ${template.functionName}`.toLowerCase().includes(filter)) continue;
        const item = document.createElement("div");
        item.className = `library-item ${template.isResolved ? "" : "unresolved"} ${state.selectedTemplateKey === template.key ? "selected" : ""}`;
        item.innerHTML = `<strong>${escapeHtml(template.functionName || "未命名")}</strong>${escapeHtml(template.ownerKey || "无 Owner")}<br>${escapeHtml(template.comment || template.parameterSignature || "无参数")}`;
        item.addEventListener("click", () => selectTemplate(template.key));
        els.libraryList.append(item);
    }
}

function selectTemplate(key) {
    state.selectedTemplateKey = key;
    renderLibrary();
    renderTemplateEditor();
}

function renderTemplateEditor() {
    const template = getSelectedTemplate();
    els.templateEditorEmpty.classList.toggle("hidden", Boolean(template));
    els.templateEditorForm.classList.toggle("hidden", !template);
    els.pinCommentList.replaceChildren();

    if (!template) return;

    els.templateName.value = template.key ?? "";
    els.templateComment.value = template.comment ?? "";

    for (const pin of template.pins ?? []) {
        const item = document.createElement("label");
        item.className = "pin-comment-item";
        const title = document.createElement("span");
        title.textContent = `${pin.direction} / ${pin.name} / ${pin.pinType}`;
        const textarea = document.createElement("textarea");
        textarea.value = pin.comment ?? "";
        textarea.dataset.pinId = pin.id;
        textarea.dataset.pinName = pin.name;
        textarea.dataset.pinDirection = pin.direction;
        textarea.placeholder = "写下这个参数的用途、默认值建议或注意事项";
        item.append(title, textarea);
        els.pinCommentList.append(item);
    }
}

async function saveSelectedTemplateComments(event) {
    event.preventDefault();
    const template = getSelectedTemplate();
    if (!template) return;

    template.comment = els.templateComment.value;
    for (const textarea of els.pinCommentList.querySelectorAll("textarea")) {
        const pin = (template.pins ?? []).find(item =>
            item.id === textarea.dataset.pinId
            || (item.name === textarea.dataset.pinName && item.direction === textarea.dataset.pinDirection));
        if (pin) {
            pin.comment = textarea.value;
        }
    }

    state.library = await postJson("/api/node-library/template", template);
    addMessage("已保存注释", template.functionName || template.key);
    renderLibrary();
    renderTemplateEditor();
}

function addTemplateNode(template) {
    if (!state.graph) return;
    const node = {
        id: `new-${crypto.randomUUID()}`,
        kind: "ContextFunctionCall",
        title: template.functionName,
        ownerKey: template.ownerKey,
        functionName: template.functionName,
        expressionType: "EX_VirtualFunction",
        position: { x: 180, y: 180 },
        payload: {
            templateKey: template.key,
            parameterSignature: template.parameterSignature,
            resolvedOwnerIndex: template.resolvedOwnerIndex,
            comment: template.comment ?? ""
        },
        pins: JSON.parse(JSON.stringify(template.pins ?? []))
    };
    for (const pin of node.pins) {
        pin.id = `${node.id}:${pin.direction}:${pin.name}`.replaceAll(" ", "-");
        pin.linkedTo = [];
    }
    state.graph.nodes.push(node);
    renderGraph();
    selectNode(node.id);
}

function getSelectedTemplate() {
    return (state.library.templates ?? []).find(template => template.key === state.selectedTemplateKey) ?? null;
}

function syncConnectionsFromPins() {
    if (!state.graph) return;
    const pinIndex = new Map();
    for (const node of state.graph.nodes) {
        for (const pin of node.pins ?? []) {
            pinIndex.set(pin.id, { node, pin });
        }
    }

    const connections = [];
    for (const node of state.graph.nodes) {
        for (const pin of node.pins ?? []) {
            if (pin.direction !== "Output") continue;
            for (const linkedId of pin.linkedTo ?? []) {
                const linked = pinIndex.get(linkedId);
                if (linked) {
                    connections.push({ fromNodeId: node.id, fromPinId: pin.id, toNodeId: linked.node.id, toPinId: linked.pin.id });
                }
            }
        }
    }
    state.graph.connections = connections;
}

function getSelectedNode() {
    return state.graph?.nodes?.find(node => node.id === state.selectedNodeId) ?? null;
}

function clearActivePins() {
    for (const dot of document.querySelectorAll(".pin-dot.active")) {
        dot.classList.remove("active");
    }
}

function addMessages(messages) {
    for (const message of messages) addMessage("提示", translateMessage(message));
}

function addMessage(title, detail) {
    const message = document.createElement("div");
    message.className = "message";
    message.innerHTML = `<strong>${escapeHtml(title)}</strong>${escapeHtml(detail)}`;
    els.messages.prepend(message);
}

function translateMessage(message) {
    return String(message)
        .replace("No usmap was supplied. Unversioned properties and some pin types may be incomplete.", "未提供 usmap。未版本化属性和部分 Pin 类型可能不完整。")
        .replace("No matching .uexp file was supplied. If this asset uses split exports, choose the uasset and uexp together or use directory upload.", "未找到匹配的 .uexp。如果该资产使用分离导出，请同时选择 uasset 与 uexp，或使用目录选择。")
        .replace("Offsets were recalculated for context/skip expressions. Explicit jump target editing is preserved from payload until label-based jumps are added.", "已重新计算 context/skip 表达式的 offset。显式 jump 目标暂时保留 payload 中的值。");
}

function setStatus(text) {
    els.status.textContent = text;
}

function coerceValue(value) {
    if (value === "true") return true;
    if (value === "false") return false;
    const number = Number(value);
    return Number.isFinite(number) && value.trim() !== "" ? number : value;
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[char]));
}

async function getJson(url) {
    const response = await fetch(url);
    return readJsonResponse(response);
}

async function postForm(url, form) {
    const response = await fetch(url, { method: "POST", body: form });
    return readJsonResponse(response);
}

async function postJson(url, body) {
    const response = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
    });
    return readJsonResponse(response);
}

async function putJson(url, body) {
    const response = await fetch(url, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
    });
    return readJsonResponse(response);
}

async function readJsonResponse(response) {
    const text = await response.text();
    const data = text ? JSON.parse(text) : {};
    if (!response.ok) {
        throw new Error(data.message ?? response.statusText);
    }
    return data;
}
