#!/usr/bin/env python3
"""
Convert Solomon VRPTW benchmark text files to JSON format
that our Unity importer can read.

Usage:
    python solomon_to_json.py c1_2_1.txt -o c1_2_1.json
    python solomon_to_json.py c1_2_1.txt  # outputs to stdout
"""
import json
import sys
import re

def parse_solomon(text, name=""):
    lines = [l.strip() for l in text.strip().split('\\n') if l.strip()]

    result = {
        "name": name,
        "vehicleCount": 50,
        "vehicleCapacity": 200,
        "mapping": {
            "centerLongitude": 121.55,
            "centerLatitude": 29.87,
            "scaleMetersPerUnit": 50.0,
            "flightHeightMeters": 80.0
        },
        "customers": []
    }

    i = 0

    # Find vehicle section
    while i < len(lines) and "VEHICLE" not in lines[i].upper():
        i += 1
    i += 1  # skip VEHICLE

    # Skip column header
    if i < len(lines) and "NUMBER" in lines[i].upper():
        i += 1

    # Parse vehicle info
    if i < len(lines):
        nums = re.findall(r'[\\d.]+', lines[i])
        if len(nums) >= 2:
            result["vehicleCount"] = int(nums[0])
            result["vehicleCapacity"] = int(nums[1])
        i += 1

    # Find customer section
    while i < len(lines) and "CUSTOMER" not in lines[i].upper():
        i += 1
    i += 1  # skip CUSTOMER

    # Skip column header
    if i < len(lines) and "CUST" in lines[i].upper():
        i += 1

    # Parse customers
    while i < len(lines):
        nums = re.findall(r'[\\d.]+', lines[i])
        if len(nums) >= 7:
            result["customers"].append({
                "id": int(nums[0]),
                "x": float(nums[1]),
                "y": float(nums[2]),
                "demand": int(nums[3]),
                "readyTime": float(nums[4]),
                "dueDate": float(nums[5]),
                "serviceTime": float(nums[6])
            })
        i += 1

    return result

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python solomon_to_json.py <input.txt> [-o output.json]")
        sys.exit(1)

    input_file = sys.argv[1]
    output_file = None

    if "-o" in sys.argv:
        idx = sys.argv.index("-o")
        if idx + 1 < len(sys.argv):
            output_file = sys.argv[idx + 1]

    with open(input_file, 'r') as f:
        text = f.read()

    name = input_file.rsplit('.', 1)[0].rsplit('/', 1)[-1].rsplit('\\', 1)[-1]
    result = parse_solomon(text, name)

    json_str = json.dumps(result, indent=2)

    if output_file:
        with open(output_file, 'w') as f:
            f.write(json_str)
        print(f"Saved to {output_file} ({len(result['customers'])} customers)")
    else:
        print(json_str)