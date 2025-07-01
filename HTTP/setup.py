from setuptools import setup, find_packages

setup(
    name="MultiAugvClientWebInterface",
    version="0.1.0",
    packages=find_packages(),
    include_package_data=True,
    install_requires=open("requirements.txt").read().splitlines(),
    entry_points={
        "console_scripts": [
            "MAugv=http.__main__:main"
        ]
    },
)