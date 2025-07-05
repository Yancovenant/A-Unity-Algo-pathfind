import os
import threading
from xml.etree import ElementTree as ET
from typing import Any, Dict

# Thread-safe cache for parsed XML templates
class TemplateCache:
    def __init__(self):
        self._cache = {}
        self._lock = threading.Lock()

    def get(self, path):
        with self._lock:
            return self._cache.get(path)

    def set(self, path, value):
        with self._lock:
            self._cache[path] = value

TEMPLATE_CACHE = TemplateCache()

class QWebRenderError(Exception):
    pass

def load_template(path: str) -> ET.Element:
    cached = TEMPLATE_CACHE.get(path)
    if cached is not None:
        return cached
    if not os.path.exists(path):
        raise QWebRenderError(f"Template not found: {path}")
    tree = ET.parse(path)
    root = tree.getroot()
    TEMPLATE_CACHE.set(path, root)
    return root

def qweb_render(template_path: str, context: Dict[str, Any] = None) -> str:
    context = context or {}
    root = load_template(template_path)
    return _render_node(root, context)

def _render_node(node, context):
    # Handle QWeb-like directives
    if node.tag == 't-set':
        name = node.attrib.get('name')
        value = _eval_expr(node.attrib.get('value', ''), context)
        context = context.copy()
        context[name] = value
        return ''
    if node.tag == 't-if':
        expr = node.attrib.get('expr')
        if _eval_expr(expr, context):
            return ''.join(_render_node(child, context) for child in node)
        else:
            return ''
    if node.tag == 't-foreach':
        expr = node.attrib.get('expr')
        as_ = node.attrib.get('as', 'item')
        items = _eval_expr(expr, context)
        out = []
        for item in items:
            ctx = context.copy()
            ctx[as_] = item
            out.append(''.join(_render_node(child, ctx) for child in node))
        return ''.join(out)
    if node.tag == 't-out':
        expr = node.attrib.get('expr')
        val = _eval_expr(expr, context)
        return str(val) if val is not None else ''
    if node.tag == 't-esc':
        expr = node.attrib.get('expr')
        val = _eval_expr(expr, context)
        return _escape(str(val)) if val is not None else ''
    # Render normal XML/HTML
    out = []
    if node.text:
        out.append(node.text)
    for child in node:
        out.append(_render_node(child, context))
        if child.tail:
            out.append(child.tail)
    return f'<{node.tag}{_render_attrs(node)}>' + ''.join(out) + f'</{node.tag}>'

def _render_attrs(node):
    attrs = []
    for k, v in node.attrib.items():
        if not k.startswith('t-'):
            attrs.append(f' {k}="{_escape(v)}"')
    return ''.join(attrs)

def _eval_expr(expr, context):
    try:
        return eval(expr, {}, context)
    except Exception as e:
        return ''

def _escape(s):
    return (s.replace('&', '&amp;')
             .replace('<', '&lt;')
             .replace('>', '&gt;')
             .replace('"', '&quot;')
             .replace("'", '&#39;')) 