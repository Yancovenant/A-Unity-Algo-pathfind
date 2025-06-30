from setuptools import setup, find_packages

setup(
    name="UnityWebInterface",
    version="0.1.0",
    packages=find_packages(),
    install_requires=open("requirements.txt").read().splitlines(),
    entry_points={
        "console_scripts": [
            "unityweb=http.__main__:main"
        ]
    },
)
