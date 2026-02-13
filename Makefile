.PHONY: test-editmode test-thunderstorm test-cloud-proto run-tests

UNITY ?= /Applications/Unity/Hub/Editor/6000.1.5f1/Unity.app/Contents/MacOS/Unity
ROOT := $(abspath $(dir $(lastword $(MAKEFILE_LIST))))
PROJECT ?= $(ROOT)
RESULTS ?= $(PROJECT)/Logs/editmode-results.xml
LOGFILE ?= $(PROJECT)/Logs/editmode-log.txt
TEST_FILTER ?= $(UNITY_TEST_FILTER)

# Set TEST_FILTER (or UNITY_TEST_FILTER) to a Unity test name/pattern.
test-editmode:
	"$(UNITY)" \
	  -batchmode \
	  -projectPath "$(PROJECT)" \
	  -runTests \
	  -testPlatform EditMode \
	  $(if $(TEST_FILTER),-testFilter "$(TEST_FILTER)",) \
	  -testResults "$(RESULTS)" \
	  -logFile "$(LOGFILE)"
	@echo "Test results: $(RESULTS)"

run-tests: test-editmode

test-thunderstorm:
	$(MAKE) test-editmode TEST_FILTER=ThunderstormDemoBuildsUpdraftAndCloud

test-cloud-proto:
	$(MAKE) test-editmode TEST_FILTER=CloudPrototypeShader_RendersAndOptionallyCaptures
