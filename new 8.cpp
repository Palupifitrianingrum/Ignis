#include <bits/stdc++.h>
using namespace std;

class Employee {
protected:
    string name;
    
public:
    Employee(const string& run) : name(run) {}
    virtual ~Employee() {}
    virtual int salary() const = 0;
    string getName() const { return name; }
};

class FullTime : public Employee {
private:
    int baseSalary;
    int bonus;
    
public:
    FullTime(const string& run, int sha, int bonusAmount) 
        : Employee(run), baseSalary(sha), bonus(bonusAmount) {}
    
    int salary() const override {
        return baseSalary + bonus;
    }
};

class PartTime : public Employee {
private:
    int hourlyRate;
    int hours;
    
public:
    PartTime(const string& run, int sha, int workHours) 
        : Employee(run), hourlyRate(sha), hours(workHours) {}
    
    int salary() const override {
        return hourlyRate * hours;
    }
};

int main() {
    ios::sync_with_stdio(false);
    cin.tie(nullptr);
    
    int Q;
    cin >> Q;
    
    vector<unique_ptr<Employee>> employees;
    
    for(int i = 0; i < Q; i++) {
        string type;
        cin >> type;
        
        if(type == "FULLTIME") {
            string run;
            int sha, bonusVal;
            cin >> run >> sha >> bonusVal;
            employees.push_back(make_unique<FullTime>(run, sha, bonusVal));
        }
        else if(type == "PARTTIME") {
            string run;
            int sha, workTime;
            cin >> run >> sha >> workTime;
            employees.push_back(make_unique<PartTime>(run, sha, workTime));
        }
    }
    
    for(const auto& emp : employees) {
        cout << emp->getName() << ": " << emp->salary() << endl;
    }
    
    return 0;
}