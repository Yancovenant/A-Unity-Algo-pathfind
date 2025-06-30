from setuptools import setup, find_packages
import os

base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
requirements_path = os.path.join(base_dir, "requirements.txt")
with open(requirements_path, "r", encoding="utf-8") as f:
    requirements = f.read().splitlines()

setup(
    name="MultiAugvClientWebInterface",
    version="0.1.0",
    packages=find_packages(where=base_dir),
    install_requires=requirements,
    entry_points={
        "console_scripts": [
            "MAugv=http.__main__:main"
        ]
    },
)
