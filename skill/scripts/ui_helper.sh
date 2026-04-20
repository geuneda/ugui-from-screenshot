#!/bin/bash
# UI 요소 생성 헬퍼: create → reparent → RectTransform fix 일괄 처리
#
# 이유: unity-cli의 ui.*.create는 parentName/parentId 파라미터를 조용히 무시해서
# 항상 Canvas 루트에 생성된다. 또한 이후 reparent는 RectTransform 값을 망가뜨린다.
# 이 스크립트는 생성 → reparent → rect 재설정을 원자적으로 수행한다.
#
# 반환값: 생성된 GameObject의 InstanceID (stdout)
#
# Usage:
#   create_ui <type> <name> <parentId> <aMinX> <aMinY> <aMaxX> <aMaxY> \
#             <pvX> <pvY> <posX> <posY> <sizeX> <sizeY> [extra_args...]
#
#   type: image | text | button 등 (ui.<type>.create)
#   parentId: 부모의 InstanceID (음수), 최상위면 Canvas의 id 사용
#
# 예시:
#   PARENT_ID=-37354  # MainCard의 id
#   HEADER_ID=$(create_ui image Header $PARENT_ID 0 1 0 1 0 1 32 -32 918 77 color=#00000000)
#   # HEADER_ID 변수에 새로 만든 Header의 InstanceID가 들어감

create_ui() {
    local type=$1
    local name=$2
    local parentId=$3
    local aMinX=$4
    local aMinY=$5
    local aMaxX=$6
    local aMaxY=$7
    local pvX=$8
    local pvY=$9
    local posX=${10}
    local posY=${11}
    local sizeX=${12}
    local sizeY=${13}
    shift 13
    local extra="$@"

    # 1. 생성 (Canvas 아래에 생김)
    local result
    result=$(unity-cli ui ${type}.create canvasName=UICanvas name=${name} ${extra} 2>&1)
    local id
    id=$(echo "$result" | python3 -c "import json,sys; d=json.loads(sys.stdin.read()); print(d['result']['id'])" 2>/dev/null)

    if [ -z "$id" ]; then
        echo "FAIL create $name: $result" >&2
        return 1
    fi

    # 2. reparent (parentId로만 동작, worldPositionStays=false 필수)
    unity-cli gameobject reparent name=${name} parentId=${parentId} worldPositionStays=false > /dev/null 2>&1

    # 3. RectTransform 재설정 (reparent가 값을 망가뜨리기 때문)
    local rectJson
    rectJson=$(python3 -c "import json; print(json.dumps({'anchorMin':{'x':${aMinX},'y':${aMinY}},'anchorMax':{'x':${aMaxX},'y':${aMaxY}},'pivot':{'x':${pvX},'y':${pvY}},'anchoredPosition':{'x':${posX},'y':${posY}},'sizeDelta':{'x':${sizeX},'y':${sizeY}}}))")
    unity-cli component update name=${name} type=UnityEngine.RectTransform values="$rectJson" > /dev/null 2>&1

    echo "$id"
}

# 이 파일을 source 가 아니라 직접 실행했을 때 함수를 호출하는 엔트리포인트
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    "$@"
fi
