"""Reject missing, failed or skipped Unity tests, including silently skipped GPU coverage."""
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

REQUIRED = {
    'PudgeWars.Tests.LoadingProgressTests.StartupSceneLoadsMenuWithAVisibleProgressBar',
    'PudgeWars.Tests.GraphicsWarmupTests.WarmupDrawsEffectsSilentlyAndRestoresTheScene',
    'PudgeWars.Tests.SdfTextTests.OutlineKeepsTheColoredFaceAndPanelVisibleAtSmallSizes',
}


def validate(directory):
    cases = []
    for path in sorted(Path(directory).rglob('*.xml')):
        root = ET.parse(path).getroot()
        if root.tag in ('test-run', 'test-results'):
            cases.extend(root.iter('test-case'))
    errors = []
    if not cases:
        errors.append('No executed Unity test cases found.')
    names = {case.get('fullname', case.get('name', '')) for case in cases}
    for name in sorted(REQUIRED - names):
        errors.append(f'Required graphics/startup test missing: {name}')
    for case in cases:
        if case.get('result') not in ('Passed', 'Success'):
            name = case.get('fullname', case.get('name', '<unnamed>'))
            reason = ' '.join(case.itertext()).strip()
            errors.append(f'{name}: {case.get("result", "unknown result")} {reason}')
    return len(cases), errors


def main():
    try:
        count, errors = validate(sys.argv[1])
    except (OSError, ET.ParseError) as error:
        print(f'Cannot read test results: {error}', file=sys.stderr)
        return 1
    print(f'Unity test cases: {count}; problems: {len(errors)}')
    for error in errors[:20]:
        print(error, file=sys.stderr)
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
