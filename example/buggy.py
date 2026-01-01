# Example buggy Python script for demo
def calculate_average(numbers):
    total = 0
    for num in numbers:
        total += num
    return total / len(numbers)  # Bug: Division by zero when list is empty

def main():
    data = []  # Empty list will cause division by zero
    result = calculate_average(data)
    print(f"Average: {result}")

if __name__ == "__main__":
    main()
