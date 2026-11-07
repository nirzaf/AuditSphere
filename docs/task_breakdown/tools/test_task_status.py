#!/usr/bin/env python3
"""Self-tests for the documentation helper only; never execute application code.
All writes use a temporary copy of this task pack.
Run: python tools/test_task_status.py
"""
from __future__ import annotations
import argparse
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

ORIGINAL=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('local_task_status',ORIGINAL/'tools/task_status.py')
helper=importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)

class TaskStatusTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)/'pack'
        shutil.copytree(ORIGINAL,self.root,ignore=shutil.ignore_patterns('__pycache__','*.pyc'))
        helper.ROOT=self.root
        (self.root/'tracking/test_evidence.md').write_text('Synthetic evidence for helper self-test only. Not application evidence.\n')
        # Lifecycle tests start from the original transition state even after the
        # live task pack advances. Only the disposable fixture is reset.
        tasks=helper.load_tasks()
        row,metadata,body=tasks['T001']
        metadata['status']='NOT_STARTED'
        for field in ('reviewer','review_decision','reviewed_commit','evidence_ref','approval_ref'):
            metadata[field]=''
        helper.write_task(self.root/row['file'],metadata,body.replace('- [x]','- [ ]'))
        row,metadata,body=tasks['T002']
        metadata['status']='NOT_STARTED'
        for field in ('owner','reviewer','review_decision','reviewed_commit','evidence_ref','approval_ref','branch'):
            metadata[field]=''
        helper.write_task(self.root/row['file'],metadata,body.replace('- [x]','- [ ]'))
        helper.refresh()
    def tearDown(self):
        helper.ROOT=ORIGINAL
        self.temp.cleanup()
    def arguments(self,task,status,**kwargs):
        defaults=dict(task=task,status=status,owner='Synthetic test owner',reviewer=None,commit=None,
          evidence=None,approval=None,review_decision=None,reason=None,branch=None,pr=None)
        defaults.update(kwargs)
        return argparse.Namespace(**defaults)
    def complete(self,task):
        tasks=helper.load_tasks();row,m,body=tasks[task]
        helper.set_status(self.arguments(task,'IN_PROGRESS'))
        helper.set_status(self.arguments(task,'IN_REVIEW'))
        m,body=helper.read_task(self.root/row['file'])
        body=body.replace('- [ ]','- [x]')
        helper.write_task(self.root/row['file'],m,body)
        helper.set_status(self.arguments(task,'COMPLETED',reviewer='Synthetic independent reviewer',
            commit='1'*40,evidence='tracking/test_evidence.md',approval='Synthetic scope approval',review_decision='APPROVED'))
    def test_initial_pack_validates(self):
        helper.refresh()
        errors,stats=helper.validate()
        self.assertEqual([],errors)
        self.assertEqual(75,stats['tasks'])
        self.assertEqual(111,stats['commands_queries'])
    def test_dependency_blocks_early_start(self):
        with self.assertRaisesRegex(ValueError,'Incomplete dependencies'):
            helper.set_status(self.arguments('T019','IN_PROGRESS'))
    def test_direct_completion_is_rejected(self):
        with self.assertRaisesRegex(ValueError,'Invalid transition'):
            helper.set_status(self.arguments('T001','COMPLETED'))
    def test_review_without_evidence_cannot_complete(self):
        helper.set_status(self.arguments('T001','IN_PROGRESS'))
        helper.set_status(self.arguments('T001','IN_REVIEW'))
        with self.assertRaisesRegex(ValueError,'reviewer|reviewed_commit|evidence'):
            helper.set_status(self.arguments('T001','COMPLETED'))
    def test_valid_completion_unlocks_next_task(self):
        self.complete('T001')
        tasks=helper.load_tasks()
        self.assertEqual('COMPLETED',tasks['T001'][1]['status'])
        self.assertEqual('READY',helper.readiness('T002',tasks))
        errors,_=helper.validate()
        self.assertEqual([],errors)
    def test_reopen_blocks_affected_started_descendants(self):
        self.complete('T001');self.complete('T002')
        helper.set_status(self.arguments('T003','IN_PROGRESS'))
        helper.set_status(self.arguments('T001','REOPENED',reason='Synthetic contract change'))
        tasks=helper.load_tasks()
        self.assertEqual('BLOCKED',tasks['T002'][1]['status'])
        self.assertEqual('BLOCKED',tasks['T003'][1]['status'])
        self.assertEqual('NOT_STARTED',tasks['T006'][1]['status'])
        self.assertEqual('tracking/test_evidence.md',tasks['T002'][1]['evidence_ref'])
        errors,_=helper.validate();self.assertEqual([],errors)
    def test_stale_index_is_detected_and_refresh_repairs_it(self):
        tasks=helper.load_tasks();row,m,b=tasks['T001']
        m['owner']='Synthetic owner';m['status']='IN_PROGRESS'
        helper.write_task(self.root/row['file'],m,b)
        errors,_=helper.validate();self.assertTrue(any('Index' in x for x in errors))
        helper.refresh();errors,_=helper.validate();self.assertEqual([],errors)
    def test_original_source_tampering_is_detected(self):
        p=self.root/'source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md'
        p.write_text(p.read_text()+'\nSynthetic tamper.\n')
        errors,_=helper.validate();self.assertIn('Original source hash mismatch',errors)
    def test_missing_link_is_detected(self):
        p=self.root/'tracking/test_evidence.md'
        p.write_text('[Broken synthetic link](does-not-exist.md)\n')
        errors,_=helper.validate();self.assertTrue(any('missing link target' in x for x in errors))
    def test_dependency_metadata_drift_is_detected(self):
        tasks=helper.load_tasks();row,m,b=tasks['T020'];m['depends_on']=[]
        helper.write_task(self.root/row['file'],m,b);helper.refresh()
        errors,_=helper.validate();self.assertTrue(any('dependency/identity drift' in x for x in errors))
    def test_same_person_review_is_rejected(self):
        helper.set_status(self.arguments('T001','IN_PROGRESS'))
        helper.set_status(self.arguments('T001','IN_REVIEW'))
        tasks=helper.load_tasks();row,m,b=tasks['T001']
        helper.write_task(self.root/row['file'],m,b.replace('- [ ]','- [x]'))
        with self.assertRaisesRegex(ValueError,'independent reviewer'):
            helper.set_status(self.arguments('T001','COMPLETED',reviewer='Synthetic test owner',
                commit='1'*40,evidence='tracking/test_evidence.md',approval='Synthetic approval',review_decision='APPROVED'))
    def test_blocker_reason_is_required(self):
        with self.assertRaisesRegex(ValueError,'reason'):
            helper.set_status(self.arguments('T001','BLOCKED'))
    def test_audit_source_tampering_is_detected(self):
        p=self.root/'source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md'
        p.write_text(p.read_text(encoding='utf-8')+'\nSynthetic tamper.\n',encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Audit source hash mismatch' in x for x in errors))
    def test_missing_audit_ac_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        lines=p.read_text(encoding='utf-8').splitlines()
        new_lines=[l for l in lines if not l.startswith('| AS-AUD-001 | `AS-AUD-001-AC01`')]
        p.write_text('\n'.join(new_lines)+'\n',encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Traceability ledger must contain 258 AC rows' in x for x in errors))
    def test_duplicate_audit_ac_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        text=p.read_text(encoding='utf-8')
        text=text.replace('`AS-AUD-001-AC02`','`AS-AUD-001-AC01`',1)
        p.write_text(text,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Duplicate AC rows' in x for x in errors))
    def test_missing_awp_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        lines=p.read_text(encoding='utf-8').splitlines()
        new_lines=[l for l in lines if not l.startswith('| `AWP-01-01`')]
        p.write_text('\n'.join(new_lines)+'\n',encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Traceability ledger must contain 165 AWP rows' in x for x in errors))
    def test_duplicate_awp_owner_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        text=p.read_text(encoding='utf-8')
        text=text.replace('\n| `AWP-01-02` |','\n| `AWP-01-01` |',1)
        p.write_text(text,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Duplicate AWP rows' in x for x in errors))
    def test_task_local_traceability_mismatch_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        text=p.read_text(encoding='utf-8')
        text=text.replace('| `AWP-01-01` | 1. Planning & Risk Assessment | Obtain company registration documents and basic company information. | AS-AUD-007 | `AS-AUD-007-AC01` | **T060** |',
                           '| `AWP-01-01` | 1. Planning & Risk Assessment | Obtain company registration documents and basic company information. | AS-AUD-007 | `AS-AUD-007-AC01` | **T061** |')
        p.write_text(text,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('local primary AWP traceability drift' in x for x in errors))
    def test_disposition_summary_drift_is_detected(self):
        p=self.root/'tracking/auditsphere-audit-tracker-workflow-traceability.md'
        text=p.read_text(encoding='utf-8')
        text=text.replace('`MERGE_EXISTING`: 59','`MERGE_EXISTING`: 58')
        p.write_text(text,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('Traceability summary mismatch for MERGE_EXISTING' in x for x in errors))
    def test_source_ids_preserve_original_procedure_meaning(self):
        errors,_=helper.validate()
        self.assertEqual([],[x for x in errors if 'source procedure meaning drift' in x])
        p=self.root/'tasks'/'20_Audit_Completion'/'auditsphere-audit-task-t075-subsequent-events-review.md'
        text=p.read_text(encoding='utf-8')
        # Reassigning a source ID's meaning removes its preserved wording and must be reported.
        tampered=text.replace('Review post-year-end bank statements and transactions',
                              'Record management inquiries about later events')
        self.assertNotEqual(text,tampered)
        p.write_text(tampered,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('source procedure meaning drift' in x and 'AWP-17-01' in x for x in errors),errors)
    def test_audit_handover_requires_declared_acceptance_inputs(self):
        p=self.root/'tasks'/'20_Audit_Completion'/'auditsphere-audit-task-t075-subsequent-events-review.md'
        text=p.read_text(encoding='utf-8')
        missing_input=text.replace('Operator handover record','handover stuff')
        self.assertNotEqual(text,missing_input)
        p.write_text(missing_input,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('audit handover gate missing declared inputs' in x and 'Operator handover record' in x for x in errors),errors)
        p.write_text(text,encoding='utf-8')
        no_gate=text.split('## Audit acceptance and handover gate')[0]+text.split('## Audit acceptance and handover gate',1)[1].split('\n## ',1)[1]
        p.write_text(no_gate,encoding='utf-8')
        errors,_=helper.validate()
        self.assertTrue(any('audit acceptance and handover gate must declare its acceptance inputs' in x for x in errors),errors)

if __name__=='__main__':unittest.main(verbosity=2)
