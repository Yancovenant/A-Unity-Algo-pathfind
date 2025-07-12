from starlette.responses import HTMLResponse

def render_layout(title, template):
    with open("static/xml/web_layout.xml", "r", encoding="utf-8") as f:
        web_layout = f.read()
    return HTMLResponse(
        web_layout.replace("<t t-title/>", title)
                  .replace("<t t-out/>", template)
    )
