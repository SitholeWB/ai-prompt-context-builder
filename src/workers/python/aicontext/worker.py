#!/usr/bin/env python3
import sys
import os
import json
import ast

def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            res = handle_request(req)
            sys.stdout.write(json.dumps(res) + "\n")
            sys.stdout.flush()
        except Exception as e:
            err_res = {
                "protocolVersion": "1.0",
                "requestId": "unknown",
                "success": False,
                "errorCode": "WorkerProtocolError",
                "errorMessage": str(e)
            }
            sys.stdout.write(json.dumps(err_res) + "\n")
            sys.stdout.flush()

def handle_request(req):
    op = req.get("operation")
    req_id = req.get("requestId", "")

    if op == "ping":
        return {
            "protocolVersion": "1.0",
            "requestId": req_id,
            "success": True,
            "adapter": {
                "id": "python",
                "version": "1.0.0",
                "capability": "Semantic"
            }
        }

    if op == "analyse":
        return analyze(req)

    return {
        "protocolVersion": "1.0",
        "requestId": req_id,
        "success": False,
        "errorMessage": f"Unsupported operation: {op}"
    }

def analyze(req):
    req_id = req.get("requestId", "")
    root_path = os.path.abspath(req.get("rootPath", ""))
    workspace_root = os.path.abspath(req.get("workspacePath") or os.path.dirname(root_path))

    nodes = []
    edges = []
    diagnostics = []
    warnings = []
    dynamic_constructs = []

    visited_files = set()
    queue = [root_path]
    visited_files.add(root_path)

    while queue:
        current_file = queue.pop(0)
        if not os.path.exists(current_file):
            continue

        rel_path = os.path.relpath(current_file, workspace_root).replace("\\", "/")
        try:
            with open(current_file, "r", encoding="utf-8") as f:
                content = f.read()
        except Exception as e:
            continue

        file_node_id = f"file:{rel_path}"
        nodes.append({
            "id": file_node_id,
            "kind": "File",
            "language": "Python",
            "analysisLevel": "Semantic",
            "displayName": os.path.basename(current_file),
            "qualifiedName": rel_path,
            "relativePath": rel_path,
            "sourceAvailable": True,
            "content": content,
            "diagnostics": [],
            "metadata": {}
        })

        # Parse AST
        try:
            tree = ast.parse(content, filename=current_file)
        except Exception as pe:
            diagnostics.append({
                "code": "PythonParseError",
                "severity": "Warning",
                "message": f"Syntax parsing failed: {str(pe)}",
                "relativePath": rel_path,
                "adapter": "PythonAnalyzer",
                "recoverable": True,
                "affectsCompleteness": False
            })
            continue

        # Inspect AST nodes
        for node in ast.walk(tree):
            # Check for dynamic importlib or getattr
            if isinstance(node, ast.Call):
                func_name = ""
                if isinstance(node.func, ast.Name):
                    func_name = node.func.id
                elif isinstance(node.func, ast.Attribute):
                    func_name = node.func.attr
                if func_name in ("import_module", "__import__", "getattr"):
                    dynamic_constructs.append(f"Dynamic invocation '{func_name}' in {rel_path}")

            # Classes
            if isinstance(node, ast.ClassDef):
                cls_name = node.name
                cls_id = f"type:{rel_path}#{cls_name}"
                nodes.append({
                    "id": cls_id,
                    "kind": "Class",
                    "language": "Python",
                    "analysisLevel": "Semantic",
                    "displayName": cls_name,
                    "qualifiedName": cls_name,
                    "relativePath": rel_path,
                    "sourceAvailable": True,
                    "diagnostics": [],
                    "metadata": {}
                })
                edges.append({
                    "sourceNodeId": file_node_id,
                    "targetNodeId": cls_id,
                    "relationship": "Export",
                    "confidence": "Verified",
                    "analysisLevel": "Semantic",
                    "metadata": {}
                })

                # Base classes
                for base in node.bases:
                    base_name = ""
                    if isinstance(base, ast.Name):
                        base_name = base.id
                    elif isinstance(base, ast.Attribute):
                        base_name = base.attr
                    if base_name:
                        # Attempt to resolve local base class
                        resolved_file = resolve_python_module(base_name, current_file, workspace_root)
                        if resolved_file:
                            link_and_queue(cls_id, resolved_file, "BaseType", edges, queue, visited_files, workspace_root)

            # Functions
            elif isinstance(node, ast.FunctionDef) or isinstance(node, ast.AsyncFunctionDef):
                fn_name = node.name
                fn_id = f"fn:{rel_path}#{fn_name}"
                nodes.append({
                    "id": fn_id,
                    "kind": "Function",
                    "language": "Python",
                    "analysisLevel": "Semantic",
                    "displayName": fn_name,
                    "qualifiedName": fn_name,
                    "relativePath": rel_path,
                    "sourceAvailable": True,
                    "diagnostics": [],
                    "metadata": {}
                })

            # Imports: import foo
            elif isinstance(node, ast.Import):
                for alias in node.names:
                    mod_name = alias.name
                    target = resolve_python_module(mod_name, current_file, workspace_root)
                    if target:
                        link_and_queue(file_node_id, target, "Import", edges, queue, visited_files, workspace_root)

            # Relative & absolute imports: from .foo import bar
            elif isinstance(node, ast.ImportFrom):
                target = None
                if node.level and node.level > 0:
                    # Relative import
                    target = resolve_relative_python_import(node.module or "", node.level, current_file, workspace_root)
                elif node.module:
                    target = resolve_python_module(node.module, current_file, workspace_root)

                if target:
                    link_and_queue(file_node_id, target, "Import", edges, queue, visited_files, workspace_root)

    if dynamic_constructs:
        warnings.append(f"Python dynamic constructs detected (may affect completeness): {', '.join(dynamic_constructs[:3])}")

    return {
        "protocolVersion": "1.0",
        "requestId": req_id,
        "success": True,
        "adapter": {
            "id": "python",
            "version": "1.0.0",
            "capability": "Semantic"
        },
        "graph": {
            "nodes": nodes,
            "edges": edges,
            "diagnostics": diagnostics,
            "warnings": warnings,
            "dynamicConstructs": dynamic_constructs
        }
    }

def resolve_python_module(mod_name, current_file, workspace_root):
    parts = mod_name.split(".")
    # 1. Search in current directory
    dir_path = os.path.dirname(current_file)
    cand = os.path.join(dir_path, *parts) + ".py"
    if os.path.exists(cand):
        return cand
    cand_init = os.path.join(dir_path, *parts, "__init__.py")
    if os.path.exists(cand_init):
        return cand_init

    # 2. Search in workspace root
    cand_ws = os.path.join(workspace_root, *parts) + ".py"
    if os.path.exists(cand_ws):
        return cand_ws
    cand_ws_init = os.path.join(workspace_root, *parts, "__init__.py")
    if os.path.exists(cand_ws_init):
        return cand_ws_init

    return None

def resolve_relative_python_import(mod_name, level, current_file, workspace_root):
    dir_path = os.path.dirname(current_file)
    for _ in range(level - 1):
        dir_path = os.path.dirname(dir_path)

    if mod_name:
        parts = mod_name.split(".")
        cand = os.path.join(dir_path, *parts) + ".py"
        if os.path.exists(cand):
            return cand
        cand_init = os.path.join(dir_path, *parts, "__init__.py")
        if os.path.exists(cand_init):
            return cand_init
    else:
        cand_init = os.path.join(dir_path, "__init__.py")
        if os.path.exists(cand_init):
            return cand_init

    return None

def link_and_queue(source_id, target_file, rel_type, edges, queue, visited, workspace_root):
    rel_path = os.path.relpath(target_file, workspace_root).replace("\\", "/")
    target_id = f"file:{rel_path}"
    edges.append({
        "sourceNodeId": source_id,
        "targetNodeId": target_id,
        "relationship": rel_type,
        "confidence": "Verified",
        "analysisLevel": "Semantic",
        "metadata": {}
    })
    if target_file not in visited:
        visited.add(target_file)
        queue.append(target_file)

if __name__ == "__main__":
    main()
