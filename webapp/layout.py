import os
from xml.etree import ElementTree as ET

def render_layout(content_file):
    layout_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'templates', 'web_layout.xml')
    content_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), 'templates', content_file)

    layout = ET.parse(layout_path).getroot()
    content = ET.parse(content_path).getroot()

    # Find placeholder tag in layout
    placeholder = layout.find(".//t-placeholder")
    if placeholder is not None:
        placeholder.clear()
        for child in content:
            placeholder.append(child)

    return ET.tostring(layout, encoding='unicode', method='html')
